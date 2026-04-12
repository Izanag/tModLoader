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
		if ((!invalid || op == null || isInvoke) && !TryGetWorldItemActiveAssignment(node, ref op, ref targetType))
			return node;

		if (targetType == null)
			targetType = model.GetEnclosingSymbol(node.SpanStart).ContainingType;

		var nameToken = node.Identifier;
		var handler = handlers.SingleOrDefault(h => nameToken.Text == h.name && targetType.InheritsFrom(h.type));
		if (handler == default)
			return node;

		return handler.handler.Invoke(this, op, node);
	}

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

	private static ExpressionSyntax GetContainingExpression(ExpressionSyntax expr)
	{
		while (true) {
			switch (expr.Parent) {
				case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == expr:
					expr = memberAccess;
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

	private static ExpressionSyntax RemovedExpression(MemberUseRewriter rw, IOperation op, ExpressionSyntax expr, string comment)
	{
		var type = op.Type ?? rw.model.GetTypeInfo(expr).Type;
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
}
