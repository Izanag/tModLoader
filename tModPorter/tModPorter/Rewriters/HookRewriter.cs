using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static tModPorter.Rewriters.SimpleSyntaxFactory;

namespace tModPorter.Rewriters;

public class HookRewriter : BaseRewriter
{
	internal delegate bool RewriteHook(ref MethodDeclarationSyntax node, IMethodSymbol sym, IMethodSymbol baseSym);

	public class RefactorEntry
	{
		public string type { get; init; }
		public string method { get; init; }
		public string comment { get; init; }

		public bool removed;
		public Dictionary<string, string> parameterRenames;

		public RefactorEntry RenameParameter(string from, string to) {
			parameterRenames ??= new();
			parameterRenames[from] = to;
			return this;
		}
	}

	private static List<RefactorEntry> refactors = new();

	private static RefactorEntry AddRefactor(string type, string method, string comment) {
		RefactorEntry entry = new() { type = type, method = method, comment = comment };
		refactors.Add(entry);
		return entry;
	}

	public static RefactorEntry ChangeHookSignature(string type, string member, string comment = null) => AddRefactor(type, member, comment);
	public static void HookRemoved(string type, string member, string comment) => AddRefactor(type, member, "Note: Removed. " + comment).removed = true;

	private static bool SelectRefactor(ISymbol sym, out RefactorEntry refactor) {
		refactor = null;
		if (!sym.IsOverride)
			return false;

		refactor = refactors.SingleOrDefault(refactor => sym.Name == refactor.method && sym.ContainingType.InheritsFrom(refactor.type));
		return refactor != null;
	}

	private static bool SelectBaseSym<T>(T sym, T overriddenSym, out T baseSym) where T : class, ISymbol {
		baseSym = overriddenSym;
		if (baseSym == null || baseSym.IsObsolete())
			baseSym = sym.ContainingType.BaseType.LookupMember<T>(sym.Name);

		return baseSym != null;
	}

	public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
		var sym = model.GetDeclaredSymbol(node);
		RegisterRemovedPropertyRewrites(sym, node);
		node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
		if (!SelectRefactor(sym, out var refactor))
			return node;

		if ((refactor.removed || RewriteSignature(ref node, sym)) && refactor.comment != null)
			node = node.WithIdentifier(node.Identifier.WithBlockComment(refactor.comment));

		return node;
	}

	public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
		var sym = model.GetDeclaredSymbol(node);
		if (ShouldRemoveNoOpModifyDamageScaling(sym, node))
			return null;
		if (ShouldRemoveDuplicateProjectileModifyHitNpc(node))
			return null;

		RegisterParameterRenames(sym, node);
		RegisterBaseMethodInvocationRewrites(sym, node);
		RegisterPreReforgeCanReforgeMigration(sym, node);
		RegisterModifyWeaponDamageBodyRewrites(sym, node);
		RegisterCatchFishBodyRewrites(sym, node);
		RegisterShootBodyRewrites(sym, node);
		RegisterUseItemBodyRewrites(sym, node);
		RegisterSaveDataBodyRewrites(sym, node);
		RegisterAddStartingItemsBodyRewrites(sym, node);
		RegisterEmptyModPrefixAllStatChangesMigration(sym, node);
		RegisterModifyHurtBodyRewrites(sym, node);
		RegisterModifyIncomingHitBodyRewrites(sym, node);
		RegisterNpcDrawScreenPosBodyRewrites(sym, node);
		RegisterSetNpcNameListBodyRewrites(sym, node);
		RegisterCanHitNpcBodyRewrites(sym, node);
		RegisterRemovedHookBodyRewrites(sym, node);
		RegisterRemovedHookStaticDefaultsMigrations(sym, node);
		node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
		if (sym?.Name == "PreReforge" && node.Identifier.Text == "CanReforge")
			return node;

		if (!SelectRefactor(sym, out var refactor))
			return node;

		if ((refactor.removed || RewriteSignature(ref node, sym)) && refactor.comment != null)
			node = node.WithParameterList(node.ParameterList.WithBlockComment(refactor.comment));

		return node;
	}

	private void RegisterRemovedPropertyRewrites(IPropertySymbol sym, PropertyDeclarationSyntax node)
	{
		if (sym?.Name == "AltTextures" && sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModNPC") && ReturnsEmptyStringArray(node))
			RegisterAction<PropertyDeclarationSyntax>(node, _ => null);

		if (sym != null &&
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") &&
			sym.Name is "IgnoreDamageModifiers" or "OnlyShootOnSwing" &&
			ReturnsLiteralFalse(node))
			RegisterAction<PropertyDeclarationSyntax>(node, _ => null);
	}

	private void RegisterPreReforgeCanReforgeMigration(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym?.Name != "PreReforge")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem")))
			return;

		if (!ReturnsLiteralBoolean(node))
			return;

		if (node.Parent is not TypeDeclarationSyntax typeDeclaration)
			return;

		if (typeDeclaration.Members.OfType<MethodDeclarationSyntax>().Any(m => m != node && m.Identifier.Text == "CanReforge"))
			return;

		RegisterAction<MethodDeclarationSyntax>(node, n =>
			n.WithIdentifier(n.Identifier.WithText("CanReforge"))
			 .WithReturnType(PredefinedType(Token(SyntaxKind.BoolKeyword)).WithTriviaFrom(n.ReturnType)));
	}

	private bool ShouldRemoveNoOpModifyDamageScaling(IMethodSymbol sym, MethodDeclarationSyntax node) =>
		sym?.Name == "ModifyDamageScaling" &&
		(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile") ||
		 sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalProjectile")) &&
		node.Body != null &&
		!node.Body.Statements.Any();

	private static bool ShouldRemoveDuplicateProjectileModifyHitNpc(MethodDeclarationSyntax node)
	{
		if (node.Identifier.Text != "ModifyHitNPC" || node.Parent is not TypeDeclarationSyntax typeDeclaration)
			return false;

		bool projectileType = typeDeclaration.BaseList?.Types.Any(t => {
			string name = t.Type.ToString();
			return name is "ModProjectile" or "GlobalProjectile";
		}) == true;

		if (!projectileType)
			return false;

		string signature = string.Join("|", node.ParameterList.Parameters.Select(p => p.Type?.ToString()));
		return typeDeclaration.Members
			.OfType<MethodDeclarationSyntax>()
			.Any(m => m != node &&
				m.Identifier.Text == node.Identifier.Text &&
				string.Join("|", m.ParameterList.Parameters.Select(p => p.Type?.ToString())) == signature &&
				m.SpanStart > node.SpanStart);
	}

	private void RegisterRemovedHookStaticDefaultsMigrations(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (!SelectRefactor(sym, out var refactor) || !refactor.removed)
			return;

		if (sym.Name == "AutoLightSelect" && sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem")) {
			RegisterAutoLightSelectMigration(node);
			return;
		}

		if (sym.Name == "DrawBehind" && sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModGore")) {
			RegisterModGoreDrawBehindMigration(node);
			return;
		}

		if (sym.Name != "SingleGrappleHook" || !sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile"))
			return;

		if (!ReturnsLiteralTrue(node))
			return;

		if (node.Parent is not TypeDeclarationSyntax typeDecl)
			return;

		RegisterAction<MethodDeclarationSyntax>(node, _ => null);

		var assignmentStatement = ExpressionStatement(
			AssignmentExpression(
				SyntaxKind.SimpleAssignmentExpression,
				ElementAccessExpression(
					MemberAccessExpression(
						MemberAccessExpression(UseType("Terraria.ID.ProjectileID"), "Sets"),
						"SingleGrappleHook"
					),
					BracketedArgumentList(SingletonSeparatedList(Argument(IdentifierName("Type"))))
				),
				LiteralExpression(SyntaxKind.TrueLiteralExpression)
			)
		);

		var setStaticDefaultsMethod = typeDecl.Members
			.OfType<MethodDeclarationSyntax>()
			.FirstOrDefault(m => m.Identifier.Text == "SetStaticDefaults" && m.ParameterList.Parameters.Count == 0);

		if (setStaticDefaultsMethod != null) {
			RegisterAction<MethodDeclarationSyntax>(setStaticDefaultsMethod, m => InsertSingleGrappleHookAssignment(m, assignmentStatement));
			return;
		}

		var newMethod = (MethodDeclarationSyntax)ParseMemberDeclaration(
@"public override void SetStaticDefaults()
	{
		ProjectileID.Sets.SingleGrappleHook[Type] = true;
	}")!;
		newMethod = newMethod
			.WithLeadingTrivia(Tab)
			.WithTrailingTrivia(CarriageReturnLineFeed);

		RegisterAction<TypeDeclarationSyntax>(typeDecl, t => t.AddMembers(newMethod));
	}

	private static bool ReturnsLiteralTrue(MethodDeclarationSyntax node) =>
		node.ExpressionBody?.Expression.IsKind(SyntaxKind.TrueLiteralExpression) == true ||
		node.Body?.Statements is [ReturnStatementSyntax { Expression.RawKind: (int)SyntaxKind.TrueLiteralExpression }];

	private static bool ReturnsLiteralBoolean(MethodDeclarationSyntax node) =>
		node.ExpressionBody?.Expression is LiteralExpressionSyntax literalExpression &&
			(literalExpression.IsKind(SyntaxKind.TrueLiteralExpression) || literalExpression.IsKind(SyntaxKind.FalseLiteralExpression)) ||
		node.Body?.Statements is [ReturnStatementSyntax { Expression: LiteralExpressionSyntax returnLiteral }] &&
			(returnLiteral.IsKind(SyntaxKind.TrueLiteralExpression) || returnLiteral.IsKind(SyntaxKind.FalseLiteralExpression));

	private static bool ReturnsLiteralNull(MethodDeclarationSyntax node) =>
		node.ExpressionBody?.Expression.IsKind(SyntaxKind.NullLiteralExpression) == true ||
		node.Body?.Statements is [ReturnStatementSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression }];

	private bool ReturnsEmptyStringArray(PropertyDeclarationSyntax node) =>
		IsEmptyStringArrayCreation(node.ExpressionBody?.Expression) ||
		node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body.Statements: [ReturnStatementSyntax { Expression: { } expression }] }] &&
			IsEmptyStringArrayCreation(expression);

	private bool ReturnsLiteralFalse(PropertyDeclarationSyntax node) =>
		node.ExpressionBody?.Expression.IsKind(SyntaxKind.FalseLiteralExpression) == true ||
		node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body.Statements: [ReturnStatementSyntax { Expression.RawKind: (int)SyntaxKind.FalseLiteralExpression }] }];

	private bool IsEmptyStringArrayCreation(ExpressionSyntax expression)
	{
		if (expression is ArrayCreationExpressionSyntax arrayCreation &&
			arrayCreation.Type.ElementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.StringKeyword } &&
			arrayCreation.Type.RankSpecifiers.Count == 1 &&
			arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
			arrayCreation.Type.RankSpecifiers[0].Sizes[0] is LiteralExpressionSyntax sizeLiteral &&
			sizeLiteral.Token.ValueText == "0" &&
			arrayCreation.Initializer == null)
			return true;

		if (expression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation && implicitArrayCreation.Initializer.Expressions.Count == 0)
			return true;

		return false;
	}

	private void RegisterAutoLightSelectMigration(MethodDeclarationSyntax node)
	{
		if (node.Body == null || node.Parent is not TypeDeclarationSyntax typeDecl)
			return;

		var assignments = TryCreateAutoLightSelectAssignments(node.Body);
		if (assignments == null)
			return;

		RegisterAction<MethodDeclarationSyntax>(node, _ => null);
		RegisterAction<TypeDeclarationSyntax>(typeDecl, t => InsertAutoLightSelectAssignments(t, assignments));
	}

	private void RegisterModGoreDrawBehindMigration(MethodDeclarationSyntax node)
	{
		if (!ReturnsLiteralTrue(node) || node.Parent is not TypeDeclarationSyntax typeDecl)
			return;

		var assignment = ExpressionStatement(
			AssignmentExpression(
				ElementAccessExpression(
					MemberAccessExpression(
						MemberAccessExpression(UseType("Terraria.ID.GoreID"), "Sets"),
						"DrawBehind"
					),
					BracketedArgumentList(SingletonSeparatedList(Argument(IdentifierName("Type"))))
				),
				LiteralExpression(SyntaxKind.TrueLiteralExpression)
			)
		);

		RegisterAction<MethodDeclarationSyntax>(node, _ => null);
		RegisterAction<TypeDeclarationSyntax>(typeDecl, t => InsertAutoLightSelectAssignments(t, new[] { assignment }));
	}

	private List<StatementSyntax> TryCreateAutoLightSelectAssignments(BlockSyntax body)
	{
		var parameterToSetName = new Dictionary<string, string>(StringComparer.Ordinal) {
			["dryTorch"] = "Torches",
			["wetTorch"] = "WaterTorches",
			["glowstick"] = "Glowsticks",
		};

		var enabledSets = new HashSet<string>(StringComparer.Ordinal);
		foreach (var statement in body.Statements) {
			if (statement is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } ||
				!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
				assignment.Left is not IdentifierNameSyntax identifier ||
				assignment.Right is not LiteralExpressionSyntax literal)
				return null;

			if (!parameterToSetName.TryGetValue(identifier.Identifier.Text, out var setName))
				return null;

			if (literal.IsKind(SyntaxKind.TrueLiteralExpression))
				enabledSets.Add(setName);
			else if (!literal.IsKind(SyntaxKind.FalseLiteralExpression))
				return null;
		}

		return enabledSets
			.Select(CreateAutoLightSelectAssignment)
			.Cast<StatementSyntax>()
			.ToList();
	}

	private ExpressionStatementSyntax CreateAutoLightSelectAssignment(string setName) =>
		ExpressionStatement(
			AssignmentExpression(
				ElementAccessExpression(
					MemberAccessExpression(
						MemberAccessExpression(UseType("Terraria.ID.ItemID"), "Sets"),
						setName
					),
					BracketedArgumentList(SingletonSeparatedList(Argument(IdentifierName("Type"))))
				),
				LiteralExpression(SyntaxKind.TrueLiteralExpression)
			)
		);

	private static MethodDeclarationSyntax InsertSingleGrappleHookAssignment(MethodDeclarationSyntax method, StatementSyntax assignmentStatement)
	{
		bool HasAssignment(StatementSyntax statement) => statement.ToString().Replace(" ", "") == assignmentStatement.ToString().Replace(" ", "");

		if (method.Body != null) {
			if (method.Body.Statements.Any(HasAssignment))
				return method;

			return method.WithBody(method.Body.AddStatements(assignmentStatement));
		}

		var body = Block(assignmentStatement);
		return method
			.WithExpressionBody(null)
			.WithSemicolonToken(default)
			.WithBody(body);
	}

	private static MethodDeclarationSyntax InsertStaticDefaultsAssignments(MethodDeclarationSyntax method, IEnumerable<StatementSyntax> assignments)
	{
		var distinctAssignments = assignments
			.Select(FormatInsertedStatement)
			.ToList();
		bool HasAssignment(StatementSyntax statement, StatementSyntax assignment) => statement.ToString().Replace(" ", "") == assignment.ToString().Replace(" ", "");

		if (method.Body != null) {
			var statementsToAdd = distinctAssignments
				.Where(assignment => !method.Body.Statements.Any(existing => HasAssignment(existing, assignment)))
				.ToArray();

			if (statementsToAdd.Length == 0)
				return method;

			return method.WithBody(method.Body.AddStatements(statementsToAdd));
		}

		var body = Block(distinctAssignments);
		return method
			.WithExpressionBody(null)
			.WithSemicolonToken(default)
			.WithBody(body);
	}

	private static StatementSyntax FormatInsertedStatement(StatementSyntax statement) =>
		statement.WithLeadingTrivia(Tab, Tab).WithTrailingTrivia(CarriageReturnLineFeed);

	private static TypeDeclarationSyntax InsertAutoLightSelectAssignments(TypeDeclarationSyntax typeDeclaration, IEnumerable<StatementSyntax> assignments)
	{
		var setStaticDefaultsMethod = typeDeclaration.Members
			.OfType<MethodDeclarationSyntax>()
			.FirstOrDefault(m => m.Identifier.Text == "SetStaticDefaults" && m.ParameterList.Parameters.Count == 0);

		if (setStaticDefaultsMethod != null) {
			var newMembers = typeDeclaration.Members.Replace(setStaticDefaultsMethod, InsertStaticDefaultsAssignments(setStaticDefaultsMethod, assignments));
			return typeDeclaration.WithMembers(newMembers);
		}

		var newMethod = (MethodDeclarationSyntax)ParseMemberDeclaration(
@"public override void SetStaticDefaults()
	{
	}")!;
		newMethod = newMethod
			.WithLeadingTrivia(Tab)
			.WithTrailingTrivia(CarriageReturnLineFeed);
		newMethod = InsertStaticDefaultsAssignments(newMethod, assignments);

		return typeDeclaration.AddMembers(newMethod);
	}

	private void RegisterModifyWeaponDamageBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (node.Body == null || sym.Name != "ModifyWeaponDamage")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer")))
			return;

		var parameterMap = sym.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter, StringComparer.Ordinal);
		if (!parameterMap.TryGetValue("add", out var addParameter) ||
			!parameterMap.TryGetValue("mult", out var multParameter) ||
			!parameterMap.TryGetValue("flat", out var flatParameter))
			return;

		foreach (var assignment in node.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
			if (assignment.Left is not IdentifierNameSyntax identifier)
				continue;

			if (model.GetSymbolInfo(identifier).Symbol is not IParameterSymbol parameter)
				continue;

			ExpressionSyntax replacementLeft = null;
			if (SymbolEqualityComparer.Default.Equals(parameter, addParameter) || SymbolEqualityComparer.Default.Equals(parameter, multParameter))
				replacementLeft = IdentifierName("damage");
			else if (SymbolEqualityComparer.Default.Equals(parameter, flatParameter))
				replacementLeft = MemberAccessExpression(IdentifierName("damage"), "Flat");

			if (replacementLeft == null)
				continue;

			RegisterAction<AssignmentExpressionSyntax>(assignment, n => n.WithLeft(replacementLeft.WithTriviaFrom(n.Left)));
		}
	}

	private void RegisterCatchFishBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (node.Body == null || sym.Name != "CatchFish" || !sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer"))
			return;

		var parameterMap = sym.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter, StringComparer.Ordinal);
		var replacements = new Dictionary<IParameterSymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);

		void Map(string parameterName, ExpressionSyntax replacement)
		{
			if (parameterMap.TryGetValue(parameterName, out var parameter))
				replacements[parameter] = replacement;
		}

		ExpressionSyntax attemptAccess(string memberName) => MemberAccessExpression(IdentifierName("attempt"), memberName);
		ExpressionSyntax conditionsAccess(string memberName) => MemberAccessExpression(attemptAccess("playerFishingConditions"), memberName);

		Map("fishingRod", conditionsAccess("Pole"));
		Map("bait", conditionsAccess("Bait"));
		Map("power", attemptAccess("fishingLevel"));
		Map("liquidType", ConditionalExpression(
			attemptAccess("inHoney"),
			LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(2)),
			ConditionalExpression(
				attemptAccess("inLava"),
				LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)),
				LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))
			)
		));
		Map("poolSize", attemptAccess("waterTilesCount"));
		Map("worldLayer", attemptAccess("heightLevel"));
		Map("questFish", attemptAccess("questFish"));

		if (replacements.Count == 0)
			return;

		foreach (var identifier in node.Body.DescendantNodes().OfType<IdentifierNameSyntax>()) {
			if (model.GetSymbolInfo(identifier).Symbol is not IParameterSymbol parameter ||
				!replacements.TryGetValue(parameter, out var replacement))
				continue;

			RegisterAction<IdentifierNameSyntax>(identifier, n => replacement.WithTriviaFrom(n));
		}
	}

	private void RegisterShootBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (node.Body == null || sym.Name != "Shoot")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer")))
			return;

		var parameterMap = sym.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter, StringComparer.Ordinal);
		var replacements = new Dictionary<IParameterSymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);

		void Map(string parameterName, ExpressionSyntax replacement)
		{
			if (parameterMap.TryGetValue(parameterName, out var parameter))
				replacements[parameter] = replacement;
		}

		Map("speedX", MemberAccessExpression(IdentifierName("velocity"), "X"));
		Map("speedY", MemberAccessExpression(IdentifierName("velocity"), "Y"));
		Map("knockBack", IdentifierName("knockback"));

		if (replacements.Count == 0)
			return;

		foreach (var identifier in node.Body.DescendantNodes().OfType<IdentifierNameSyntax>()) {
			if (model.GetSymbolInfo(identifier).Symbol is not IParameterSymbol parameter ||
				!replacements.TryGetValue(parameter, out var replacement))
				continue;

			if (identifier.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument && argument.Expression == identifier)
				continue;

			RegisterAction<IdentifierNameSyntax>(identifier, n => replacement.WithTriviaFrom(n));
		}
	}

	private void RegisterUseItemBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "UseItem")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem")))
			return;

		if (node.Body != null) {
			foreach (var returnStatement in node.Body.Statements.OfType<ReturnStatementSyntax>()) {
				if (returnStatement.Expression?.IsKind(SyntaxKind.FalseLiteralExpression) != true)
					continue;

				RegisterAction<ReturnStatementSyntax>(returnStatement, n =>
					n.WithExpression(LiteralExpression(SyntaxKind.NullLiteralExpression).WithTriviaFrom(n.Expression)));
			}
		}

		if (node.ExpressionBody?.Expression.IsKind(SyntaxKind.FalseLiteralExpression) == true) {
			RegisterAction<ArrowExpressionClauseSyntax>(node.ExpressionBody, n =>
				n.WithExpression(LiteralExpression(SyntaxKind.NullLiteralExpression).WithTriviaFrom(n.Expression)));
		}
	}

	private void RegisterSaveDataBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "SaveData")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModTileEntity")))
			return;

		if (node.Body?.Statements is [ReturnStatementSyntax { Expression: { } expression }] &&
			IsEmptyTagCompoundCreation(expression)) {
			RegisterAction<MethodDeclarationSyntax>(node, CreateEmptySaveDataMethod);
			return;
		}

		if (node.ExpressionBody?.Expression is { } arrowExpression && IsEmptyTagCompoundCreation(arrowExpression)) {
			RegisterAction<MethodDeclarationSyntax>(node, CreateEmptySaveDataMethod);
		}
	}

	private bool IsEmptyTagCompoundCreation(ExpressionSyntax expression)
	{
		if (expression is not ObjectCreationExpressionSyntax objectCreation)
			return false;

		if (objectCreation.Initializer?.Expressions.Count > 0)
			return false;

		return model.GetTypeInfo(objectCreation).Type?.InheritsFrom("Terraria.ModLoader.IO.TagCompound") == true;
	}

	private static MethodDeclarationSyntax CreateEmptySaveDataMethod(MethodDeclarationSyntax node)
	{
		var trailingTrivia = node.Body?.CloseBraceToken.TrailingTrivia ?? node.SemicolonToken.TrailingTrivia;
		var body = Block().WithCloseBraceToken(Token(TriviaList(), SyntaxKind.CloseBraceToken, trailingTrivia));
		return node.WithBody(body).WithExpressionBody(null).WithSemicolonToken(default);
	}

	private void RegisterAddStartingItemsBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "AddStartingItems" || !sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer") || node.Body == null)
			return;

		if (sym.Parameters.Length == 1 && HasAddStartingItemsBoolOverload(node)) {
			RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (!TryCreateAddStartingItemsReturnBody(node.Body, out var newBody))
			return;

		RegisterAction<MethodDeclarationSyntax>(node, n => n.WithBody(newBody).WithExpressionBody(null).WithSemicolonToken(default));
	}

	private bool HasAddStartingItemsBoolOverload(MethodDeclarationSyntax node)
	{
		if (node.Parent is not TypeDeclarationSyntax typeDeclaration)
			return false;

		foreach (var sibling in typeDeclaration.Members.OfType<MethodDeclarationSyntax>()) {
			if (sibling == node)
				continue;

			if (model.GetDeclaredSymbol(sibling) is not IMethodSymbol siblingSymbol)
				continue;

			if (siblingSymbol.Name != "AddStartingItems" || siblingSymbol.Parameters.Length != 2)
				continue;

			if (siblingSymbol.Parameters[0].Name == "items" && siblingSymbol.Parameters[1].Type.SpecialType == SpecialType.System_Boolean)
				return true;
		}

		return false;
	}

	private bool TryCreateAddStartingItemsReturnBody(BlockSyntax body, out BlockSyntax newBody)
	{
		var itemExpressions = new List<string>();

		foreach (var statement in body.Statements) {
			if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } ||
				invocation.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax listIdentifier, Name.Identifier.Text: "Add" } memberAccess ||
				invocation.ArgumentList.Arguments.Count != 1 ||
				model.GetSymbolInfo(listIdentifier).Symbol is not IParameterSymbol itemsParameter ||
				itemsParameter.Name != "items")
				goto Fail;

			var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
			var argumentType = model.GetTypeInfo(argumentExpression).Type;
			var rewrittenExpression = argumentType?.InheritsFrom("Terraria.Item") == true
				? argumentExpression.WithoutTrivia().ToString()
				: $"new Item({argumentExpression.WithoutTrivia()})";

			itemExpressions.Add(rewrittenExpression);
			continue;

		Fail:
			newBody = null;
			return false;
		}

		var trailingTrivia = body.CloseBraceToken.TrailingTrivia;
		var returnStatement = (ReturnStatementSyntax)ParseStatement($"return [{string.Join(", ", itemExpressions)}];");
		newBody = Block(returnStatement).WithCloseBraceToken(Token(TriviaList(), SyntaxKind.CloseBraceToken, trailingTrivia));
		return true;
	}

	private void RegisterModifyHurtBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "ModifyHurt" || !sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer"))
			return;

		if (!ReturnsLiteralFalse(node))
			return;

		RegisterAction<MethodDeclarationSyntax>(node, CreateEmptyModifyHurtMethod);
	}

	private static bool ReturnsLiteralFalse(MethodDeclarationSyntax node) =>
		node.ExpressionBody?.Expression.IsKind(SyntaxKind.FalseLiteralExpression) == true ||
		node.Body?.Statements is [ReturnStatementSyntax { Expression.RawKind: (int)SyntaxKind.FalseLiteralExpression }];

	private static MethodDeclarationSyntax CreateEmptyModifyHurtMethod(MethodDeclarationSyntax node)
	{
		var trailingTrivia = node.Body?.CloseBraceToken.TrailingTrivia ?? node.SemicolonToken.TrailingTrivia;
		var body = Block().WithCloseBraceToken(Token(TriviaList(), SyntaxKind.CloseBraceToken, trailingTrivia));
		return node.WithBody(body).WithExpressionBody(null).WithSemicolonToken(default);
	}

	private void RegisterModifyIncomingHitBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "ModifyIncomingHit")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModNPC") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalNPC")))
			return;

		if (!ReturnsLiteralFalse(node))
			return;

		RegisterAction<MethodDeclarationSyntax>(node, CreateEmptyModifyIncomingHitMethod);
	}

	private static MethodDeclarationSyntax CreateEmptyModifyIncomingHitMethod(MethodDeclarationSyntax node)
	{
		var trailingTrivia = node.Body?.CloseBraceToken.TrailingTrivia ?? node.SemicolonToken.TrailingTrivia;
		var body = Block().WithCloseBraceToken(Token(TriviaList(), SyntaxKind.CloseBraceToken, trailingTrivia));
		return node.WithBody(body).WithExpressionBody(null).WithSemicolonToken(default);
	}

	private void RegisterNpcDrawScreenPosBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (node.Body == null || (sym.Name != "PreDraw" && sym.Name != "PostDraw"))
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModNPC") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalNPC")))
			return;

		foreach (var memberAccess in node.Body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()) {
			if (memberAccess.Expression is not IdentifierNameSyntax { Identifier.Text: "Main" } ||
				memberAccess.Name.Identifier.Text != "screenPosition")
				continue;

			RegisterAction<MemberAccessExpressionSyntax>(memberAccess, n => IdentifierName("screenPos").WithTriviaFrom(n));
		}
	}

	private void RegisterSetNpcNameListBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "SetNPCNameList" || !sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModNPC"))
			return;

		if (node.Body != null) {
			foreach (var returnStatement in node.Body.Statements.OfType<ReturnStatementSyntax>()) {
				if (returnStatement.Expression == null || returnStatement.Expression is CollectionExpressionSyntax)
					continue;

				RegisterAction<ReturnStatementSyntax>(returnStatement, n =>
					n.WithExpression(ParseExpression($"[{n.Expression.WithoutTrivia()}]").WithTriviaFrom(n.Expression)));
			}
		}

		if (node.ExpressionBody != null && node.ExpressionBody.Expression is not CollectionExpressionSyntax) {
			RegisterAction<ArrowExpressionClauseSyntax>(node.ExpressionBody, n =>
				n.WithExpression(ParseExpression($"[{n.Expression.WithoutTrivia()}]").WithTriviaFrom(n.Expression)));
		}
	}

	private void RegisterCanHitNpcBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (sym.Name != "CanHitNPC")
			return;

		if (!(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModNPC") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalNPC")))
			return;

		if (node.Body != null) {
			foreach (var returnStatement in node.Body.Statements.OfType<ReturnStatementSyntax>()) {
				if (returnStatement.Expression?.IsKind(SyntaxKind.NullLiteralExpression) != true)
					continue;

				RegisterAction<ReturnStatementSyntax>(returnStatement, n =>
					n.WithExpression(LiteralExpression(SyntaxKind.TrueLiteralExpression).WithTriviaFrom(n.Expression)));
			}
		}

		if (node.ExpressionBody?.Expression.IsKind(SyntaxKind.NullLiteralExpression) == true) {
			RegisterAction<ArrowExpressionClauseSyntax>(node.ExpressionBody, n =>
				n.WithExpression(LiteralExpression(SyntaxKind.TrueLiteralExpression).WithTriviaFrom(n.Expression)));
		}
	}

	private void RegisterRemovedHookBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (!SelectRefactor(sym, out var refactor) || !refactor.removed)
			return;

		if (sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPrefix") &&
			sym.Name == "AutoStaticDefaults") {
			if (node.Body != null && !node.Body.Statements.Any())
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if ((sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem")) &&
			sym.Name is "DrawHead" or "DrawBody" or "DrawLegs") {
			if (ReturnsLiteralTrue(node))
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if ((sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem")) &&
			sym.Name is "DrawHands" or "DrawHair") {
			if (!node.Body.Statements.Any())
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if ((sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModItem") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalItem")) &&
			sym.Name == "CanBurnInLava") {
			if (ReturnsLiteralNull(node))
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if ((sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalProjectile")) &&
			sym.Name == "SingleGrappleHook") {
			if (ReturnsLiteralNull(node))
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer") &&
			sym.Name is "ModifyHitPvp" or "OnHitPvp" or "ModifyHitPvpWithProj" or "OnHitPvpWithProj") {
			if (node.Body != null && !node.Body.Statements.Any())
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if ((sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile") ||
			sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalProjectile")) &&
			sym.Name is "ModifyHitPvp" or "OnHitPvp") {
			if (node.Body != null && !node.Body.Statements.Any())
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile") &&
			sym.Name == "ModifyFishingLine") {
			if (node.Body != null && !node.Body.Statements.Any())
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (sym.Name == "SetMapBackgroundImage" && sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModPlayer")) {
			if (ReturnsLiteralNull(node))
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (sym.Name == "DrawBehind" && sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModGore")) {
			if (ReturnsLiteralFalse(node))
				RegisterAction<MethodDeclarationSyntax>(node, _ => null);
			return;
		}

		if (sym.Name != "DrawBehind" || !(sym.ContainingType.InheritsFrom("Terraria.ModLoader.ModProjectile") || sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalProjectile")))
			return;

		var drawLayerTarget = sym.ContainingType.InheritsFrom("Terraria.ModLoader.GlobalProjectile")
			? IdentifierName("projectile")
			: IdentifierName("Projectile");

		foreach (var invoke in node.Body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
			if (invoke.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax listName, Name.Identifier.Text: "Add" } ||
				invoke.ArgumentList.Arguments.Count != 1)
				continue;

			string layerName = listName.Identifier.Text switch {
				"drawCacheProjsBehindNPCsAndTiles" or "behindNPCsAndTiles" => "BehindNPCsAndTiles",
				"drawCacheProjsBehindNPCs" or "behindNPCs" => "BehindNPCs",
				"drawCacheProjsBehindProjectiles" or "behindProjectiles" => "BehindProjectiles",
				"drawCacheProjsOverPlayers" or "overPlayers" => "OverPlayers",
				"drawCacheProjsOverWiresUI" or "overWiresUI" => "OverWiresUI",
				_ => null,
			};

			if (layerName == null)
				continue;

			var replacement = AssignmentExpression(
				SyntaxKind.SimpleAssignmentExpression,
				MemberAccessExpression(drawLayerTarget.WithoutTrivia(), "drawLayer"),
				MemberAccessExpression(UseType("Terraria.ID.ProjectileDrawLayerID"), layerName)
			).WithTriviaFrom(invoke);

			RegisterAction<InvocationExpressionSyntax>(invoke, _ => replacement);
		}
	}

	private void RegisterEmptyModPrefixAllStatChangesMigration(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		bool isModPrefix = sym?.ContainingType.InheritsFrom("Terraria.ModLoader.ModPrefix") == true ||
			(node.Parent as TypeDeclarationSyntax)?.BaseList?.Types.Any(t => t.Type.ToString() == "ModPrefix") == true;

		if (!isModPrefix)
			return;

		bool renamedValidateItem = sym?.Name == "ValidateItem" || node.Identifier.Text == "AllStatChangesHaveEffectOn";
		if (!renamedValidateItem || node.Body == null || node.Body.Statements.Any())
			return;

		RegisterAction<MethodDeclarationSyntax>(node, _ => null);
	}

	private void RegisterParameterRenames(IMethodSymbol sym, MethodDeclarationSyntax node) {
		if (!SelectRefactor(sym, out var refactor) || refactor.removed || refactor.parameterRenames == null)
			return;

		var renames = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
		foreach (var param in sym.Parameters) {
			if (refactor.parameterRenames.TryGetValue(param.Name, out var newName))
				renames[param] = newName;
		}

		if (renames.Count == 0)
			return;

		var body = (SyntaxNode)node.Body ?? node.ExpressionBody;
		if (body == null)
			return;

		foreach (var nameSyntax in body.DescendantNodes().OfType<IdentifierNameSyntax>()) {
			if (model.GetSymbolInfo(nameSyntax).Symbol is IParameterSymbol param && renames.TryGetValue(param, out var newName))
				RegisterAction<IdentifierNameSyntax>(nameSyntax, n => n.WithIdentifier(newName));
		}
	}

	private void RegisterBaseMethodInvocationRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (!SelectRefactor(sym, out var refactor) || refactor.removed)
			return;

		var body = (SyntaxNode)node.Body ?? node.ExpressionBody;
		if (body == null)
			return;

		foreach (var invoke in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
			// Match base.<MethodName>(...)
			if (invoke.Expression is not MemberAccessExpressionSyntax memberAccess ||
				memberAccess.Expression is not BaseExpressionSyntax ||
				memberAccess.Name.Identifier.Text != sym.Name) {
				if (invoke.Expression is not MemberAccessExpressionSyntax otherMemberAccess ||
					otherMemberAccess.Expression is not BaseExpressionSyntax)
					continue;

				memberAccess = otherMemberAccess;
			}

			var existingArgs = invoke.ArgumentList?.Arguments.ToArray() ?? Array.Empty<ArgumentSyntax>();
			var targetSym = sym.Name == memberAccess.Name.Identifier.Text && sym.Parameters.Length == existingArgs.Length
				? sym
				: sym.ContainingType.GetMembers(memberAccess.Name.Identifier.Text)
					.OfType<IMethodSymbol>()
					.FirstOrDefault(candidate => SelectRefactor(candidate, out var candidateRefactor) && !candidateRefactor.removed && candidate.Parameters.Length == existingArgs.Length);

			if (targetSym == null || !SelectBaseSym(targetSym, targetSym.OverriddenMethod, out var baseSym))
				continue;

			if (ParametersEqual(targetSym, baseSym))
				continue;

			var newParamCount = baseSym.Parameters.Length;
			if (newParamCount <= 0 || existingArgs.Length != targetSym.Parameters.Length)
				continue;

			var matchedParameters = MatchParameters(targetSym.Parameters.ToArray(), baseSym.Parameters.ToArray());
			var matchedNewParameters = matchedParameters.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
			var builtArgs = new ArgumentSyntax[newParamCount];

			// Initialize built args with identifier arguments using the base parameter names and ref/out keywords when necessary
			for (int i = 0; i < newParamCount; i++) {
				var p = baseSym.Parameters[i];

				if (matchedNewParameters.TryGetValue(i, out int oldIndex)) {
					builtArgs[i] = existingArgs[oldIndex];
					continue;
				}

				var arg = Argument(IdentifierName(p.Name));
				if (p.RefKind == RefKind.Ref)
					arg = arg.WithRefKindKeyword(TokenSpace(SyntaxKind.RefKeyword));
				else if (p.RefKind == RefKind.Out)
					arg = arg.WithRefKindKeyword(TokenSpace(SyntaxKind.OutKeyword));
				builtArgs[i] = arg;
			}

			var newArgList = ArgumentList(SimpleSyntaxFactory.SeparatedList(builtArgs)).WithTriviaFrom(invoke.ArgumentList);
			RegisterAction<InvocationExpressionSyntax>(invoke, n => n.WithArgumentList(newArgList));
		}
	}

	private bool AccessibilityMismatch(ISymbol sym, ISymbol baseSym) =>
		sym.DeclaredAccessibility != baseSym.DeclaredAccessibility;

	private bool TypeMismatch(ITypeSymbol t1, ITypeSymbol t2) =>
		!model.Compilation.ClassifyConversion(t1, t2).IsIdentity;

	private static bool ParametersEqual(IMethodSymbol sym1, IMethodSymbol sym2) =>
		sym1.Parameters.SequenceEqual(sym2.Parameters, (p1, p2) => SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type) && p1.RefKind == p2.RefKind);

	private static bool ParametersCompatible(IParameterSymbol oldParam, IParameterSymbol newParam) =>
		SymbolEqualityComparer.Default.Equals(oldParam.Type, newParam.Type) && oldParam.RefKind == newParam.RefKind;

	private static Dictionary<int, int> MatchParameters(IParameterSymbol[] oldParameters, IParameterSymbol[] newParameters)
	{
		var matches = new Dictionary<int, int>();
		if (oldParameters.Length == 0 || newParameters.Length == 0)
			return matches;

		int[,] bestScores = new int[oldParameters.Length + 1, newParameters.Length + 1];
		for (int oldIndex = oldParameters.Length - 1; oldIndex >= 0; oldIndex--) {
			for (int newIndex = newParameters.Length - 1; newIndex >= 0; newIndex--) {
				int bestScore = Math.Max(bestScores[oldIndex + 1, newIndex], bestScores[oldIndex, newIndex + 1]);
				int matchScore = ParameterMatchScore(oldParameters[oldIndex], newParameters[newIndex]);
				if (matchScore > 0)
					bestScore = Math.Max(bestScore, matchScore + bestScores[oldIndex + 1, newIndex + 1]);

				bestScores[oldIndex, newIndex] = bestScore;
			}
		}

		int i = 0;
		int j = 0;
		while (i < oldParameters.Length && j < newParameters.Length) {
			if (bestScores[i, j] == bestScores[i, j + 1]) {
				j++;
				continue;
			}

			int matchScore = ParameterMatchScore(oldParameters[i], newParameters[j]);
			if (matchScore > 0 && bestScores[i, j] == matchScore + bestScores[i + 1, j + 1]) {
				matches[i] = j;
				i++;
				j++;
				continue;
			}

			i++;
		}

		return matches;
	}

	private static int ParameterMatchScore(IParameterSymbol oldParam, IParameterSymbol newParam) =>
		!ParametersCompatible(oldParam, newParam) ? 0 :
		oldParam.Name == newParam.Name ? 2 : 1;

	private bool RewriteModifiers(ISymbol sym, ISymbol baseSym, SyntaxTokenList modifiers, out SyntaxTokenList newModifiers) {
		if (!AccessibilityMismatch(sym, baseSym)) {
			newModifiers = default;
			return false;
		}

		newModifiers = ModifierList(baseSym.DeclaredAccessibility);
		if (newModifiers.Any())
			newModifiers = newModifiers.Replace(newModifiers.Last(), newModifiers.Last().WithTrailingTrivia(Space));

		newModifiers = newModifiers
			.Add(Token(SyntaxKind.OverrideKeyword))
			.WithTriviaFrom(modifiers);

		return true;
	}

	private bool RewriteSignature(ref MethodDeclarationSyntax node, IMethodSymbol sym) {
		if (!SelectBaseSym(sym, sym.OverriddenMethod, out var baseSym))
			return false;

		var origNode = node;
		SelectRefactor(sym, out var refactor);
		bool hasParameterRenames = refactor?.parameterRenames?.Count > 0;
		if (!ParametersEqual(sym, baseSym) || hasParameterRenames) {
			var matchedParameters = MatchParameters(sym.Parameters.ToArray(), baseSym.Parameters.ToArray());
			var matchedNewParameters = matchedParameters.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
			var rewrittenParameters = new List<ParameterSyntax>(baseSym.Parameters.Length);
			var usedParameterNames = new HashSet<string>(StringComparer.Ordinal);

			for (int i = 0; i < baseSym.Parameters.Length; i++) {
				var rewrittenParameter = Parameter(baseSym.Parameters[i]);
				if (matchedNewParameters.TryGetValue(i, out int oldIndex)) {
					var oldParameter = node.ParameterList.Parameters[oldIndex];
					var parameterName = oldParameter.Identifier.Text;
					if (refactor?.parameterRenames != null && refactor.parameterRenames.TryGetValue(parameterName, out var renamedParameter))
						parameterName = renamedParameter;

					if (usedParameterNames.Contains(parameterName))
						parameterName = rewrittenParameter.Identifier.Text;
					usedParameterNames.Add(parameterName);
					rewrittenParameter = oldParameter
						.WithType(rewrittenParameter.Type.WithTriviaFrom(oldParameter.Type))
						.WithModifiers(rewrittenParameter.Modifiers)
						.WithIdentifier(rewrittenParameter.Identifier.WithText(parameterName).WithTriviaFrom(oldParameter.Identifier));
				}
				else {
					usedParameterNames.Add(rewrittenParameter.Identifier.Text);
				}

				rewrittenParameters.Add(rewrittenParameter);
			}

			node = node.WithParameterList(ParameterList(rewrittenParameters).WithTriviaFrom(node.ParameterList));
		}

		if (TypeMismatch(sym.ReturnType, baseSym.ReturnType))
			node = node.WithReturnType(UseType(baseSym.ReturnType).WithTriviaFrom(node.ReturnType));

		if (RewriteModifiers(sym, baseSym, node.Modifiers, out var newModifiers))
			node = node.WithModifiers(newModifiers);

		return node != origNode;
	}

	private bool RewriteSignature(ref PropertyDeclarationSyntax node, IPropertySymbol sym) {
		if (!SelectBaseSym(sym, sym.OverriddenProperty, out var baseSym))
			return false;

		var origNode = node;
		if (TypeMismatch(sym.Type, baseSym.Type))
			node = node.WithType(UseType(baseSym.Type).WithTriviaFrom(node.Type));

		if (RewriteModifiers(sym, baseSym, node.Modifiers, out var newModifiers))
			node = node.WithModifiers(newModifiers);

		return node != origNode;
	}
}
