using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static tModPorter.Rewriters.SimpleSyntaxFactory;

namespace tModPorter.Rewriters;

public class MemberUseRewriter : BaseRewriter {

	public delegate SyntaxNode RewriteMemberUse(MemberUseRewriter rw, IOperation op, IdentifierNameSyntax memberName);

	private static List<(string type, string name, RewriteMemberUse handler)> handlers = new();

	public static void RefactorInstanceMember(string type, string name, RewriteMemberUse handler) => handlers.Add((type, name, handler));
	public static void RefactorInstanceMember(string type, string name, AddComment comment) => RefactorInstanceMember(type, name, (_, _, n) => comment.Apply(n));

	public static void RefactorStaticMember(string type, string name, RewriteMemberUse handler) => handlers.Add((type, name, handler));
	public static void RefactorStaticMember(string type, string name, AddComment comment) => RefactorStaticMember(type, name, (_, _, n) => comment.Apply(n));


	public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node) {
		bool invalid = IdentifierNameInvalid(node, out var op, out var targetType, out bool isInvoke);
		if ((!invalid || isInvoke) && !TryGetWorldItemActiveAssignment(node, ref op, ref targetType))
			return node;

		if (op == null && !CanRewriteWithoutOperation(node))
			return node;

		if (targetType == null)
			targetType = model.GetEnclosingSymbol(node.SpanStart).ContainingType;

		var nameToken = node.Identifier;
		var handler = handlers.SingleOrDefault(h => nameToken.Text == h.name && targetType.InheritsFrom(h.type));
		if (handler == default)
			return node;

		return handler.handler.Invoke(this, op, node);
	}

	private static bool CanRewriteWithoutOperation(IdentifierNameSyntax node) =>
		node.FirstAncestorOrSelf<ExpressionStatementSyntax>() != null ||
		node.FirstAncestorOrSelf<VariableDeclaratorSyntax>() != null;

	private bool TryGetWorldItemActiveAssignment(IdentifierNameSyntax node, ref IOperation op, ref ITypeSymbol targetType)
	{
		if (node.Identifier.Text != "active" ||
			node.Parent is not MemberAccessExpressionSyntax memberAccess ||
			memberAccess.Parent is not AssignmentExpressionSyntax assignment ||
			assignment.Left != memberAccess)
			return false;

		targetType = model.GetTypeInfo(memberAccess.Expression).Type;
		if (targetType == null || !targetType.InheritsFrom("Terraria.WorldItem"))
			return false;

		op = model.GetOperation(memberAccess) ?? model.GetOperation(assignment);
		return op != null;
	}

	public static RewriteMemberUse DamageTypeField(string className, string comment = null) => (rw, op, memberName) => {
		var damageClassExpr = MemberAccessExpression(rw.UseType("Terraria.ModLoader.DamageClass"), className);

		if (op.Parent is IAssignmentOperation assign && assign.Target == op) {
			var expr = assign.Syntax;
			if (assign.Value is not ILiteralOperation { ConstantValue.Value: bool constantValue })
				return memberName.WithBlockComment("Suggestion: DamageType = ..."); // some other literal assignment

			if (!constantValue) { // = false
				rw.RegisterAction(assign.Syntax, n => n.WithBlockComment("Suggestion: Remove. See Item.DamageType"));
				return memberName;
			}

			rw.RegisterAction(assign.Value.Syntax, n => damageClassExpr.WithTriviaFrom(n).WithBlockComment(comment));
			return memberName.WithIdentifier("DamageType");
		}
		else { // plain identifier or member access
			
			var rootExpr = (ExpressionSyntax)op.Syntax;
			rw.RegisterAction<ExpressionSyntax>(rootExpr, n => InvocationExpression(n.WithoutTrivia(), damageClassExpr).WithTriviaFrom(n));
			return memberName.WithIdentifier("CountsAsClass");
		}
	};

	public static RewriteMemberUse DamageModifier(string className, string methodName, string subMember = null) => (rw, op, memberName) => {
		var damageClassExpr = MemberAccessExpression(rw.UseType("Terraria.ModLoader.DamageClass"), className);

		var rootExpr = (ExpressionSyntax)op.Syntax;
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n => {
			n = InvocationExpression(n.WithoutTrivia(), damageClassExpr).WithTriviaFrom(n);
			if (subMember != null)
				n = MemberAccessExpression(n.WithoutTrivia(), subMember).WithTriviaFrom(n);

			return n;
		});

		return memberName.WithIdentifier(methodName);
	};

	public static RewriteMemberUse ExtraJumpField(string extraJumpName, string stateFieldName) => (rw, op, memberName) => {
		var extraJumpExpr = MemberAccessExpression(rw.UseType("Terraria.ModLoader.ExtraJump"), extraJumpName);

		var rootExpr = (ExpressionSyntax)op.Syntax;
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n => {
			n = InvocationExpression(n.WithoutTrivia(), extraJumpExpr).WithTriviaFrom(n);
			n = MemberAccessExpression(n.WithoutTrivia(), stateFieldName).WithTriviaFrom(n);
			return n;
		});

		memberName = memberName.WithIdentifier("GetJumpState");

		if (op.Parent is IAssignmentOperation assign && assign.Target == op) {
			if (stateFieldName == "Active") {
				rw.RegisterAction(assign.Syntax, n => n.WithBlockComment("Suggestion: Remove. Active cannot be assigned a value."));
				return memberName;
			}

			if (stateFieldName == "Enabled") {
				rw.RegisterAction(assign.Syntax, n => n.WithBlockComment("Suggestion: Call Enable() if setting this to true, otherwise call Disable()."));
				return memberName;
			}

			var expr = assign.Syntax;
			if (assign.Value is not ILiteralOperation { ConstantValue.Value: bool })
				return memberName.WithBlockComment($"Suggestion: Player.GetJumpState(ExtraJump.{extraJumpName}).{stateFieldName} = ..."); // some other literal assignment
		}

		return memberName;
	};

	public static RewriteMemberUse DifficultyScaleCurveSample(string curveName) => (rw, op, memberName) => {
		var curveExpr = MemberAccessExpression(rw.UseType("Terraria.DataStructures.GameDifficultyData"), curveName);
		var difficultyExpr = MemberAccessExpression(rw.UseType("Terraria.Main"), "Difficulty");
		var rootExpr = (ExpressionSyntax)op.Syntax;

		rw.RegisterAction<ExpressionSyntax>(rootExpr, n =>
			InvocationExpression(MemberAccessExpression(curveExpr, "Sample"), difficultyExpr).WithTriviaFrom(n)
		);

		return memberName;
	};

	public static RewriteMemberUse ReplaceWithExpression(System.Func<MemberUseRewriter, ExpressionSyntax> replacementFactory) => (rw, op, memberName) => {
		var rootExpr = GetContainingExpression(memberName);
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n => replacementFactory(rw).WithTriviaFrom(n));
		return memberName;
	};

	public static RewriteMemberUse NpcDebuffImmunitySets() => (rw, op, memberName) => {
		if (memberName.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax expressionStatement ||
			expressionStatement.Expression is not InvocationExpressionSyntax invocation ||
			invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Add" } ||
			invocation.ArgumentList.Arguments.Count != 2)
			return memberName.WithBlockComment("Removed: See the porting notes in https://github.com/tModLoader/tModLoader/pull/3453");

		if (!TryGetSpecificDebuffImmunityAssignments(invocation, out var assignments))
			return memberName.WithBlockComment("Removed: See the porting notes in https://github.com/tModLoader/tModLoader/pull/3453");

		StatementSyntax replacement = assignments.Count == 1
			? assignments[0]
			: Block(assignments);

		rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n => replacement.WithTriviaFrom(n));
		return memberName;
	};

	public static RewriteMemberUse ModPropertiesInitializer() => (rw, op, memberName) => {
		if (memberName.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax expressionStatement ||
			expressionStatement.Expression is not AssignmentExpressionSyntax assignment ||
			assignment.Left is not IdentifierNameSyntax { Identifier.Text: "Properties" } ||
			assignment.Right is not ObjectCreationExpressionSyntax { Initializer.Expressions: var initializerExpressions })
			return memberName.WithBlockComment("Removed: Instead, assign the properties directly (ContentAutoloadingEnabled, GoreAutoloadingEnabled, MusicAutoloadingEnabled, and BackgroundAutoloadingEnabled)");

		if (!TryCreateModPropertyAssignments(initializerExpressions, out var assignments))
			return memberName.WithBlockComment("Removed: Instead, assign the properties directly (ContentAutoloadingEnabled, GoreAutoloadingEnabled, MusicAutoloadingEnabled, and BackgroundAutoloadingEnabled)");

		StatementSyntax replacement = assignments.Count == 1
			? assignments[0]
			: Block(assignments);

		rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n => replacement.WithTriviaFrom(n));
		return memberName;
	};

	public static RewriteMemberUse ModBuffCanBeCleared() => (rw, op, memberName) => {
		var assignment = memberName.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
		if (assignment?.Parent is ExpressionStatementSyntax expressionStatement &&
			assignment.Left is IdentifierNameSyntax { Identifier.Text: "canBeCleared" } &&
			assignment.Right is LiteralExpressionSyntax literal) {
			if (literal.IsKind(SyntaxKind.FalseLiteralExpression)) {
				var replacement = ParseStatement("BuffID.Sets.NurseCannotRemoveDebuff[Type] = true;");
				rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n => replacement.WithTriviaFrom(n));
				return memberName;
			}

			if (literal.IsKind(SyntaxKind.TrueLiteralExpression)) {
				rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n =>
					EmptyStatement().WithTriviaFrom(n).WithBlockComment("Note: Removed. canBeCleared defaults to true.")
				);
				return memberName;
			}
		}

		var rootExpr = GetContainingExpression(memberName);
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n =>
			PrefixUnaryExpression(
				SyntaxKind.LogicalNotExpression,
				ElementAccessExpression(
					MemberAccessExpression(
						MemberAccessExpression(rw.UseType("Terraria.ID.BuffID"), "Sets"),
						"NurseCannotRemoveDebuff"
					),
					BracketedArgumentList(SingletonSeparatedList(Argument(IdentifierName("Type"))))
				)
			).WithTriviaFrom(n)
		);

		return memberName;
	};

	public static RewriteMemberUse ModBuffLongerExpertDebuff() => (rw, op, memberName) => {
		var assignment = memberName.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
		if (assignment?.Parent is ExpressionStatementSyntax expressionStatement &&
			assignment.Left is IdentifierNameSyntax { Identifier.Text: "longerExpertDebuff" } &&
			assignment.Right is LiteralExpressionSyntax literal) {
			if (literal.IsKind(SyntaxKind.TrueLiteralExpression)) {
				var replacement = ParseStatement("BuffID.Sets.BuffTimeIsExtendedWithGameDifficulty[Type] = true;");
				rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n => replacement.WithTriviaFrom(n));
				return memberName;
			}

			if (literal.IsKind(SyntaxKind.FalseLiteralExpression)) {
				rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n =>
					EmptyStatement().WithTriviaFrom(n).WithBlockComment("Note: Removed. BuffTimeIsExtendedWithGameDifficulty defaults to false.")
				);
				return memberName;
			}
		}

		var rootExpr = GetContainingExpression(memberName);
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n =>
			ElementAccessExpression(
				MemberAccessExpression(
					MemberAccessExpression(rw.UseType("Terraria.ID.BuffID"), "Sets"),
					"BuffTimeIsExtendedWithGameDifficulty"
				),
				BracketedArgumentList(SingletonSeparatedList(Argument(IdentifierName("Type"))))
			).WithTriviaFrom(n)
		);

		return memberName;
	};

	public static RewriteMemberUse BuffBasicMountData() => (rw, op, memberName) => {
		if (memberName.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is not AssignmentExpressionSyntax assignment ||
			assignment.Parent is not ExpressionStatementSyntax expressionStatement ||
			assignment.Left is not ElementAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "BasicMountData" }, ArgumentList.Arguments.Count: 1 } elementAccess ||
			assignment.Right is not ObjectCreationExpressionSyntax { Initializer.Expressions: var initializerExpressions })
			return memberName.WithBlockComment("Removed: Replace with BuffID.Sets.MountType[Type] = ModContent.MountType<MyMount>();");

		var mountAssignment = initializerExpressions
			.OfType<AssignmentExpressionSyntax>()
			.FirstOrDefault(a => a.Left is IdentifierNameSyntax { Identifier.Text: "mountID" });
		if (mountAssignment == null)
			return memberName.WithBlockComment("Removed: Replace with BuffID.Sets.MountType[Type] = ModContent.MountType<MyMount>();");

		var buffIndexExpr = elementAccess.ArgumentList.Arguments[0].Expression;
		var replacement = ParseStatement($"BuffID.Sets.MountType[{buffIndexExpr}] = {mountAssignment.Right};");
		rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n => replacement.WithTriviaFrom(n));
		return memberName;
	};

	public static RewriteMemberUse ReplaceContainingMemberAccess(System.Func<ExpressionSyntax, ExpressionSyntax> replacementFactory) => (rw, op, memberName) => {
		if (memberName.Parent is not MemberAccessExpressionSyntax access)
			return memberName;

		var rootExpr = GetContainingExpression(access);
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n => replacementFactory(access.Expression.WithoutTrivia()).WithTriviaFrom(n));
		return memberName;
	};

	public static RewriteMemberUse ReplaceContainingMemberAccessWithNegatedMember(string memberName) =>
		ReplaceContainingMemberAccess(expr =>
			PrefixUnaryExpression(
				SyntaxKind.LogicalNotExpression,
				MemberAccessExpression(expr, memberName)
			)
		);

	public static RewriteMemberUse ReplaceWithScopedPlayerDrawSetMember(string memberName) => (rw, op, identifier) => {
		var drawSetExpr = FindScopedPlayerDrawSetExpression(rw, identifier);
		if (drawSetExpr == null)
			return identifier.WithBlockComment($"Suggestion: <PlayerDrawSet>.{memberName}");

		var rootExpr = identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier
			? (ExpressionSyntax)access
			: GetContainingExpression(identifier);
		rw.RegisterAction<ExpressionSyntax>(rootExpr, n => MemberAccessExpression(drawSetExpr.WithoutTrivia(), memberName).WithTriviaFrom(n));
		return identifier;
	};

	private static ExpressionSyntax GetContainingExpression(ExpressionSyntax expr)
	{
		while (true) {
			switch (expr.Parent) {
				case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == expr:
					expr = memberAccess;
					continue;
				case MemberAccessExpressionSyntax memberAccessName when memberAccessName.Name == expr:
					expr = memberAccessName;
					continue;
				case ElementAccessExpressionSyntax elementAccess when elementAccess.Expression == expr:
					expr = elementAccess;
					continue;
				case ConditionalAccessExpressionSyntax conditionalAccess when conditionalAccess.Expression == expr:
					expr = conditionalAccess;
					continue;
				case ParenthesizedExpressionSyntax parenthesized when parenthesized.Expression == expr:
					expr = parenthesized;
					continue;
				default:
					return expr;
			}
		}
	}

	private static ExpressionSyntax FindScopedPlayerDrawSetExpression(MemberUseRewriter rw, SyntaxNode node)
	{
		var candidates = rw.model.LookupSymbols(node.SpanStart)
			.Select(symbol => symbol switch {
				IParameterSymbol parameter when parameter.Type.InheritsFrom("Terraria.DataStructures.PlayerDrawSet") => new { parameter.Name, Priority = 0 },
				ILocalSymbol local when local.Type.InheritsFrom("Terraria.DataStructures.PlayerDrawSet") => new { local.Name, Priority = 1 },
				_ => null
			})
			.Where(candidate => candidate != null)
			.OrderBy(candidate => candidate.Priority)
			.ThenBy(candidate => candidate.Name, System.StringComparer.Ordinal)
			.FirstOrDefault();

		return candidates == null ? null : IdentifierName(candidates.Name);
	}

	private static ExpressionSyntax RemovedExpression(MemberUseRewriter rw, IOperation op, ExpressionSyntax expr, string comment)
	{
		var typeInfo = rw.model.GetTypeInfo(expr);
		var type = op.Type;
		if (type == null || type.TypeKind == TypeKind.Error)
			type = typeInfo.ConvertedType ?? typeInfo.Type;

		ExpressionSyntax replacement = type != null
			? DefaultExpression(rw.UseType(type))
			: LiteralExpression(SyntaxKind.NullLiteralExpression);

		return replacement.WithTriviaFrom(expr).WithBlockComment(comment);
	}

	public static RewriteMemberUse RemoveContainingStatementOrInitializer(string comment) => (rw, op, memberName) => {
		string fullComment = ("Note: Removed. " + comment).TrimEnd();
		var assignment = memberName.FirstAncestorOrSelf<AssignmentExpressionSyntax>();

		if (assignment?.Parent is InitializerExpressionSyntax init) {
			rw.RegisterAction<InitializerExpressionSyntax>(init, n =>
				n.WithExpressions(SyntaxFactory.SeparatedList(n.Expressions.Where(expr => expr != assignment))).WithTriviaFrom(n)
			);

			return memberName.WithBlockComment(fullComment);
		}

		var expressionStatement = memberName.FirstAncestorOrSelf<ExpressionStatementSyntax>();
		if (expressionStatement != null) {
			rw.RegisterAction<ExpressionStatementSyntax>(expressionStatement, n =>
				EmptyStatement().WithTriviaFrom(n).WithBlockComment(fullComment)
			);

			return memberName;
		}

		var variableDeclarator = memberName.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
		if (variableDeclarator?.Parent is VariableDeclarationSyntax variableDeclaration &&
			variableDeclarator.Initializer?.Value != null &&
			variableDeclarator.Initializer.Value.Span.Contains(memberName.Span) &&
			variableDeclaration.Parent is LocalDeclarationStatementSyntax localDeclaration) {
			if (variableDeclaration.Type.IsVar) {
				rw.RegisterAction<LocalDeclarationStatementSyntax>(localDeclaration, n =>
					EmptyStatement().WithTriviaFrom(n).WithBlockComment(fullComment)
				);
				return memberName;
			}

			rw.RegisterAction<EqualsValueClauseSyntax>(variableDeclarator.Initializer, n =>
				n.WithValue(DefaultExpression(variableDeclaration.Type.WithoutTrivia()).WithTriviaFrom(n.Value).WithBlockComment(fullComment))
			);
			return memberName;
		}

		var rootExpression = GetContainingExpression(memberName);
		rw.RegisterAction<ExpressionSyntax>(rootExpression, n => RemovedExpression(rw, op, n, fullComment));
		return memberName;
	};

	public static RewriteMemberUse WorldItemActive() => (rw, op, memberName) => {
		if (memberName.Parent is not MemberAccessExpressionSyntax access)
			return memberName;

		bool isAssignmentTarget =
			op.Parent is IAssignmentOperation { Target: var target } && target == op ||
			access.Parent is AssignmentExpressionSyntax assignmentSyntax && assignmentSyntax.Left == access;

		if (!isAssignmentTarget)
			return memberName;

		rw.RegisterAction<MemberAccessExpressionSyntax>(access, n =>
			MemberAccessExpression(MemberAccessExpression(n.Expression.WithoutTrivia(), "inner"), "active").WithTriviaFrom(n)
		);

		return memberName;
	};

	public static SyntaxNode RewriteIsJourneyMode(MemberUseRewriter rw, IOperation op, IdentifierNameSyntax memberName)
	{
		// memberName corresponds to the identifier "GameModeInfo" in an expression like Main.GameModeInfo.IsJourneyMode
		if (memberName.Parent is MemberAccessExpressionSyntax innerAccess && innerAccess.Parent is MemberAccessExpressionSyntax outerAccess) {
			if (outerAccess.Name.Identifier.Text == "IsJourneyMode") {
				// Replace the outer access (Main.GameModeInfo.IsJourneyMode) with Main.IsJourneyMode
				rw.RegisterAction<MemberAccessExpressionSyntax>(outerAccess, n =>
					MemberAccessExpression(innerAccess.Expression.WithoutTrivia(), "IsJourneyMode").WithTriviaFrom(n)
				);
			}
		}
		return memberName;
	}

	private static bool TryGetSpecificDebuffImmunityAssignments(InvocationExpressionSyntax invocation, out List<StatementSyntax> assignments)
	{
		assignments = null;
		var npcIndexExpr = invocation.ArgumentList.Arguments[0].Expression;
		var configExpr = invocation.ArgumentList.Arguments[1].Expression;
		if (configExpr is not ObjectCreationExpressionSyntax { Initializer.Expressions: var initializerExpressions })
			return false;

		var specificallyImmuneToAssignment = initializerExpressions
			.OfType<AssignmentExpressionSyntax>()
			.FirstOrDefault(a => a.Left is IdentifierNameSyntax { Identifier.Text: "SpecificallyImmuneTo" });
		if (specificallyImmuneToAssignment == null)
			return false;

		if (!TryGetArrayElements(specificallyImmuneToAssignment.Right, out var buffExpressions) || buffExpressions.Count == 0)
			return false;

		assignments = buffExpressions
			.Select(buffExpr => (StatementSyntax)ParseStatement($"NPCID.Sets.SpecificDebuffImmunity[{npcIndexExpr}][{buffExpr}] = true;"))
			.ToList();
		return true;
	}

	private static bool TryCreateModPropertyAssignments(SeparatedSyntaxList<ExpressionSyntax> initializerExpressions, out List<StatementSyntax> assignments)
	{
		var propertyMap = new Dictionary<string, string> {
			["Autoload"] = "ContentAutoloadingEnabled",
			["AutoloadBackgrounds"] = "BackgroundAutoloadingEnabled",
			["AutoloadGores"] = "GoreAutoloadingEnabled",
			["AutoloadSounds"] = "MusicAutoloadingEnabled",
		};

		assignments = new();
		foreach (var expression in initializerExpressions) {
			if (expression is not AssignmentExpressionSyntax assignment ||
				assignment.Left is not IdentifierNameSyntax identifier ||
				!propertyMap.TryGetValue(identifier.Identifier.Text, out var replacementProperty))
				return false;

			assignments.Add(ParseStatement($"{replacementProperty} = {assignment.Right.WithoutTrivia()};"));
		}

		return true;
	}

	private static bool TryGetArrayElements(ExpressionSyntax expression, out List<ExpressionSyntax> elements)
	{
		elements = null;
		switch (expression) {
			case ArrayCreationExpressionSyntax { Initializer.Expressions: var explicitElements }:
				elements = explicitElements.ToList();
				return true;
			case ImplicitArrayCreationExpressionSyntax { Initializer.Expressions: var implicitElements }:
				elements = implicitElements.ToList();
				return true;
			case InitializerExpressionSyntax { Expressions: var initializerElements }:
				elements = initializerElements.ToList();
				return true;
			default:
				return false;
		}
	}
}
