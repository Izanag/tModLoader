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
		node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
		if (!SelectRefactor(sym, out var refactor))
			return node;

		if ((refactor.removed || RewriteSignature(ref node, sym)) && refactor.comment != null)
			node = node.WithIdentifier(node.Identifier.WithBlockComment(refactor.comment));

		return node;
	}

	public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
		var sym = model.GetDeclaredSymbol(node);
		RegisterParameterRenames(sym, node);
		RegisterBaseMethodInvocationRewrites(sym, node);
		RegisterRemovedHookBodyRewrites(sym, node);
		RegisterRemovedHookStaticDefaultsMigrations(sym, node);
		node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
		if (!SelectRefactor(sym, out var refactor))
			return node;

		if ((refactor.removed || RewriteSignature(ref node, sym)) && refactor.comment != null)
			node = node.WithParameterList(node.ParameterList.WithBlockComment(refactor.comment));

		return node;
	}

	private void RegisterRemovedHookStaticDefaultsMigrations(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (!SelectRefactor(sym, out var refactor) || !refactor.removed)
			return;

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

	private void RegisterRemovedHookBodyRewrites(IMethodSymbol sym, MethodDeclarationSyntax node)
	{
		if (!SelectRefactor(sym, out var refactor) || !refactor.removed || node.Body == null)
			return;

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
		if (!ParametersEqual(sym, baseSym)) {
			var matchedParameters = MatchParameters(sym.Parameters.ToArray(), baseSym.Parameters.ToArray());
			var matchedNewParameters = matchedParameters.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
			var rewrittenParameters = new List<ParameterSyntax>(baseSym.Parameters.Length);

			for (int i = 0; i < baseSym.Parameters.Length; i++) {
				var rewrittenParameter = Parameter(baseSym.Parameters[i]);
				if (matchedNewParameters.TryGetValue(i, out int oldIndex)) {
					var oldParameter = node.ParameterList.Parameters[oldIndex];
					rewrittenParameter = oldParameter
						.WithType(rewrittenParameter.Type.WithTriviaFrom(oldParameter.Type))
						.WithModifiers(rewrittenParameter.Modifiers);
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
