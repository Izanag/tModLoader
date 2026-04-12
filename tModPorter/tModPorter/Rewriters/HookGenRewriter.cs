using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static tModPorter.Rewriters.SimpleSyntaxFactory;

namespace tModPorter.Rewriters;

public class HookGenRewriter : BaseRewriter {

	private List<string> refactoredUsingPrefixes = new();

	private static bool IsCreativeSacrificeOrigType(TypeSyntax typeSyntax)
	{
		var text = typeSyntax.ToString();
		return text.EndsWith("On_CreativeUI.orig_SacrificeItem_refItem_refInt32_bool")
			|| text.EndsWith("On_CreativeUI.orig_SacrificeItem_refItem_refInt32_bool_bool");
	}

	private static TypeSyntax RewriteCreativeSacrificeOrigType(TypeSyntax typeSyntax)
	{
		var text = typeSyntax.ToString();
		const string oldSuffix = "On_CreativeUI.orig_SacrificeItem_refItem_refInt32_bool";
		const string newSuffix = "On_CreativeUI.orig_SacrificeItem_refItem_refInt32_bool_bool";

		if (!text.EndsWith(oldSuffix))
			return typeSyntax;

		var prefix = text[..^oldSuffix.Length];
		return Name(prefix + newSuffix).WithTriviaFrom(typeSyntax);
	}

	public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node) {
		if (!IdentifierNameInvalid(node, out var op, out var targetType, out bool isInvoke))
			return node;

		if (op != null && targetType != null)
			return node;

		var newType = refactoredUsingPrefixes.Select(pre => model.Compilation.GetTypeByMetadataName(pre + node.Identifier.Text)).Where(t => t != null).FirstOrDefault();
		if (newType == null)
			return node;

		return IdentifierName(newType.Name).WithTriviaFrom(node);
	}

	public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
	{
		node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

		if (node.ParameterList.Parameters.Count != 4 || !IsCreativeSacrificeOrigType(node.ParameterList.Parameters[0].Type))
			return node;

		var parameters = node.ParameterList.Parameters.Replace(node.ParameterList.Parameters[0], node.ParameterList.Parameters[0].WithType(RewriteCreativeSacrificeOrigType(node.ParameterList.Parameters[0].Type)));
		var selfParameter = SyntaxFactory.Parameter(Identifier("self"))
			.WithType(UseType("Terraria.GameContent.Creative.CreativeUI").WithTrailingTrivia(Space));
		var finalParameter = SyntaxFactory.Parameter(Identifier("onlySacrificeIfItWouldFinishResearch"))
			.WithType(PredefinedType(Token(SyntaxKind.BoolKeyword)).WithTrailingTrivia(Space));

		node = node.WithParameterList(node.ParameterList.WithParameters(parameters.Insert(1, selfParameter).Add(finalParameter)).NormalizeWhitespace());

		if (node.Body == null)
			return node;

		var origInvocations = node.Body.DescendantNodes()
			.OfType<InvocationExpressionSyntax>()
			.Where(invoke => invoke.Expression is IdentifierNameSyntax { Identifier.Text: "orig" } && invoke.ArgumentList.Arguments.Count == 3)
			.ToArray();

		node = node.ReplaceNodes(origInvocations, (_, n) => {
			var args = n.ArgumentList.Arguments;
			return n.WithArgumentList(n.ArgumentList.WithArguments(args.Insert(0, Argument(IdentifierName("self"))).Add(Argument(IdentifierName("onlySacrificeIfItWouldFinishResearch")))).NormalizeWhitespace());
		});

		return node;
	}

	public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)	{
		if (!IsFullySimple(node) || !MatchQualifiedTypeName(node.ToString(), out var newName))
			return base.VisitMemberAccessExpression(node);

		return Name(newName).WithTriviaFrom(node);
	}

	private static bool IsFullySimple(MemberAccessExpressionSyntax node) => node.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
		(node.Expression is MemberAccessExpressionSyntax mAccess && IsFullySimple(mAccess) || node.Expression is IdentifierNameSyntax);

	public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node) {
		if (model.GetSymbolInfo(node).Symbol != null)
			return node;

		if (!MatchQualifiedTypeName(node.ToString(), out var newName))
			return base.VisitQualifiedName(node);

		return Name(newName).WithTriviaFrom(node);
	}

	protected override SyntaxList<UsingDirectiveSyntax> VisitUsingList(SyntaxList<UsingDirectiveSyntax> usings) {
		var renamed = usings.Where(u => u.Name != null && MatchOldHookgenNamespace(u.Name.ToString(), out _, out _)).ToArray();
		if (renamed.Length == 0)
			return usings;

		usings = List(usings.Except(renamed));
		foreach (var u in renamed) {
			MatchOldHookgenNamespace(u.Name.ToString(), out string new_ns, out string prefix);
			usings = usings.WithUsingNamespace(new_ns);

			refactoredUsingPrefixes.Add(new_ns + '.' + prefix + '_');
		}

		return base.VisitUsingList(usings);
	}

	private bool MatchQualifiedTypeName(string name, out string newName)
	{
		newName = null;
		if (!MatchOldHookgenNamespace(name, out string new_ns, out string prefix))
			return false;

		newName = new_ns.Insert(new_ns.LastIndexOf('.') + 1, prefix + '_');
		return model.Compilation.GetTypeByMetadataName(newName) != null;
	}

	private static bool MatchOldHookgenNamespace(string ns, out string new_ns, out string prefix)
	{
		if (ns.StartsWith("IL.") || ns.StartsWith("On.")) {
			prefix = ns[..2];
			new_ns = ns[3..];
			return true;
		}

		prefix = null;
		new_ns = null;
		return false;
	}
}
