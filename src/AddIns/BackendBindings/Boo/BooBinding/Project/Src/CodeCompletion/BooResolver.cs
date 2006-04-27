// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Daniel Grunwald" email="daniel@danielgrunwald.de"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections;
using System.Collections.Generic;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Dom;
using Boo.Lang.Compiler;
using AST = Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.IO;
using Boo.Lang.Compiler.Steps;
using NRResolver = ICSharpCode.SharpDevelop.Dom.NRefactoryResolver.NRefactoryResolver;

namespace Grunwald.BooBinding.CodeCompletion
{
	public class BooResolver : IResolver
	{
		#region Fields and properties
		ICompilationUnit cu;
		IProjectContent pc;
		int caretLine;
		int caretColumn;
		IClass callingClass;
		IMethodOrProperty callingMember;
		
		public IClass CallingClass {
			get {
				return callingClass;
			}
		}
		
		public IMethodOrProperty CallingMember {
			get {
				return callingMember;
			}
		}
		
		public int CaretLine {
			get {
				return caretLine;
			}
		}
		
		public int CaretColumn {
			get {
				return caretColumn;
			}
		}
		
		public IProjectContent ProjectContent {
			get {
				return pc;
			}
		}
		
		public ICompilationUnit CompilationUnit {
			get {
				return cu;
			}
		}
		
		/// <summary>
		/// Gets if duck typing is enabled for the Boo project.
		/// </summary>
		public bool IsDucky {
			get {
				BooProject p = pc.Project as BooProject;
				if (p != null)
					return p.Ducky;
				else
					return false;
			}
		}
		#endregion
		
		#region Initialization
		bool Initialize(string fileName, int caretLine, int caretColumn)
		{
			ParseInformation parseInfo = ParserService.GetParseInformation(fileName);
			if (parseInfo == null) {
				return false;
			}
			this.cu = parseInfo.MostRecentCompilationUnit;
			if (cu == null) {
				return false;
			}
			this.pc = cu.ProjectContent;
			this.caretLine = caretLine;
			this.caretColumn = caretColumn;
			this.callingClass = GetCallingClass(pc);
			callingMember = ResolveCurrentMember(callingClass);
			if (callingMember == null) {
				if (cu != parseInfo.BestCompilationUnit) {
					IClass olderClass = GetCallingClass(parseInfo.BestCompilationUnit.ProjectContent);
					if (olderClass != null && callingClass == null) {
						this.callingClass = olderClass;
					}
					callingMember = ResolveCurrentMember(olderClass);
				}
			}
			return true;
		}
		
		IClass GetCallingClass(IProjectContent pc)
		{
			IClass callingClass = cu.GetInnermostClass(caretLine, caretColumn);
			if (callingClass == null) {
				if (cu.Classes.Count == 0) return null;
				callingClass = cu.Classes[cu.Classes.Count - 1];
				if (!callingClass.Region.IsEmpty) {
					if (callingClass.Region.BeginLine > caretLine)
						callingClass = null;
				}
			}
			return callingClass;
		}
		
		IMethodOrProperty ResolveCurrentMember(IClass callingClass)
		{
			//LoggingService.DebugFormatted("Getting current method... caretLine = {0}, caretColumn = {1}", caretLine, caretColumn);
			if (callingClass == null) return null;
			IMethodOrProperty best = null;
			int line = 0;
			foreach (IMethod m in callingClass.Methods) {
				if (m.Region.BeginLine <= caretLine && m.Region.BeginLine > line) {
					line = m.Region.BeginLine;
					best = m;
				}
			}
			foreach (IProperty m in callingClass.Properties) {
				if (m.Region.BeginLine <= caretLine && m.Region.BeginLine > line) {
					line = m.Region.BeginLine;
					best = m;
				}
			}
			if (callingClass.Region.IsEmpty) {
				// maybe we are in Main method?
				foreach (IMethod m in callingClass.Methods) {
					if (m.Region.IsEmpty && !m.IsSynthetic) {
						// the main method
						if (best == null || best.BodyRegion.EndLine < caretLine)
							return m;
					}
				}
			}
			return best;
		}
		#endregion
		
		#region GetTypeOfExpression
		public IReturnType GetTypeOfExpression(AST.Expression expr, IClass callingClass)
		{
			AST.Node node = expr;
			AST.LexicalInfo lexInfo;
			do {
				if (node == null) return null;
				lexInfo = node.LexicalInfo;
				node = node.ParentNode;
			} while (lexInfo == null || lexInfo.FileName == null);
			if (!Initialize(lexInfo.FileName, lexInfo.Line, lexInfo.Column))
				return null;
			if (callingClass != null)
				this.callingClass = callingClass;
			ResolveVisitor visitor = new ResolveVisitor(this);
			visitor.Visit(expr);
			if (visitor.ResolveResult == null)
				return null;
			else
				return visitor.ResolveResult.ResolvedType;
		}
		#endregion
		
		#region GetCurrentBooMethod
		AST.Node GetCurrentBooMethod()
		{
			if (callingMember == null)
				return null;
			// TODO: don't save boo's AST in userdata, but parse fileContent here
			return callingMember.UserData as AST.Node;
		}
		#endregion
		
		#region Resolve
		public ResolveResult Resolve(ExpressionResult expressionResult,
		                             int caretLineNumber, int caretColumn,
		                             string fileName, string fileContent)
		{
			if (!Initialize(fileName, caretLineNumber, caretColumn))
				return null;
			LoggingService.Debug("Resolve " + expressionResult.ToString());
			if (expressionResult.Expression == "__GlobalNamespace") { // used for "import" completion
				return new NamespaceResolveResult(callingClass, callingMember, "");
			}
			
			AST.Expression expr;
			try {
				expr = Boo.Lang.Parser.BooParser.ParseExpression("expression", expressionResult.Expression);
			} catch (Exception ex) {
				LoggingService.Debug("Boo expression parser: " + ex.Message);
				return null;
			}
			if (expr == null)
				return null;
			if (expr is AST.IntegerLiteralExpression)
				return new IntegerLiteralResolveResult(callingClass, callingMember);
			
			if (expressionResult.Context == ExpressionFinder.BooAttributeContext.Instance) {
				AST.MethodInvocationExpression mie = expr as AST.MethodInvocationExpression;
				if (mie != null)
					expr = mie.Target;
				string name = expr.ToCodeString();
				IReturnType rt = pc.SearchType(name, 0, callingClass, cu, caretLine, caretColumn);
				if (rt != null && rt.GetUnderlyingClass() != null)
					return new TypeResolveResult(callingClass, callingMember, rt);
				rt = pc.SearchType(name + "Attribute", 0, callingClass, cu, caretLine, caretColumn);
				if (rt != null && rt.GetUnderlyingClass() != null)
					return new TypeResolveResult(callingClass, callingMember, rt);
				if (BooProject.BooCompilerPC != null) {
					IClass c = BooProject.BooCompilerPC.GetClass("Boo.Lang." + char.ToUpper(name[0]) + name.Substring(1) + "Attribute");
					if (c != null)
						return new TypeResolveResult(callingClass, callingMember, c);
				}
				string namespaceName = pc.SearchNamespace(name, callingClass, cu, caretLine, caretColumn);
				if (namespaceName != null) {
					return new NamespaceResolveResult(callingClass, callingMember, namespaceName);
				}
				return null;
			} else {
				if (expr.NodeType == AST.NodeType.ReferenceExpression) {
					// this could be a macro
					if (BooProject.BooCompilerPC != null) {
						string name = ((AST.ReferenceExpression)expr).Name;
						IClass c = BooProject.BooCompilerPC.GetClass("Boo.Lang." + char.ToUpper(name[0]) + name.Substring(1) + "Macro");
						if (c != null)
							return new TypeResolveResult(callingClass, callingMember, c);
					}
				}
			}
			
			ResolveVisitor visitor = new ResolveVisitor(this);
			visitor.Visit(expr);
			ResolveResult result = visitor.ResolveResult;
			if (expressionResult.Context == ExpressionContext.Type && result is MixedResolveResult)
				result = (result as MixedResolveResult).TypeResult;
			return result;
		}
		
		public IReturnType ConvertType(AST.TypeReference typeRef)
		{
			return ConvertVisitor.CreateReturnType(typeRef, callingClass, callingMember,
			                                       caretLine, caretColumn, pc);
		}
		
		public IField FindLocalVariable(string name, bool acceptImplicit)
		{
			VariableLookupVisitor vlv = new VariableLookupVisitor(this, name, acceptImplicit);
			vlv.Visit(GetCurrentBooMethod());
			return vlv.Result;
		}
		#endregion
		
		#region CtrlSpace
		static IClass GetPrimitiveClass(IProjectContent pc, string systemType, string newName)
		{
			IClass c = pc.GetClass(systemType);
			if (c == null) {
				LoggingService.Warn("Could not find " + systemType);
				return null;
			}
			DefaultClass c2 = new DefaultClass(c.CompilationUnit, newName);
			c2.ClassType = c.ClassType;
			c2.Modifiers = c.Modifiers;
			c2.Documentation = c.Documentation;
			c2.BaseTypes.AddRange(c.BaseTypes);
			c2.Methods.AddRange(c.Methods);
			c2.Fields.AddRange(c.Fields);
			c2.Properties.AddRange(c.Properties);
			c2.Events.AddRange(c.Events);
			return c2;
		}
		
		public ArrayList CtrlSpace(int caretLine, int caretColumn, string fileName, string fileContent, ExpressionContext context)
		{
			ArrayList result = new ArrayList();
			
			if (!Initialize(fileName, caretLine, caretColumn))
				return null;
			if (context == ExpressionContext.Importable) {
				pc.AddNamespaceContents(result, "", pc.Language, true);
				NRResolver.AddUsing(result, pc.DefaultImports, pc);
				return result;
			}
			
			NRResolver.AddContentsFromCalling(result, callingClass, callingMember);
			AddImportedNamespaceContents(result);
			
			if (BooProject.BooCompilerPC != null) {
				if (context == ExpressionFinder.BooAttributeContext.Instance) {
					foreach (object o in BooProject.BooCompilerPC.GetNamespaceContents("Boo.Lang")) {
						IClass c = o as IClass;
						if (c != null && c.Name.EndsWith("Attribute") && !c.IsAbstract) {
							result.Add(GetPrimitiveClass(BooProject.BooCompilerPC, c.FullyQualifiedName, c.Name.Substring(0, c.Name.Length - 9).ToLowerInvariant()));
						}
					}
				} else {
					foreach (object o in BooProject.BooCompilerPC.GetNamespaceContents("Boo.Lang")) {
						IClass c = o as IClass;
						if (c != null && c.Name.EndsWith("Macro") && !c.IsAbstract) {
							result.Add(GetPrimitiveClass(BooProject.BooCompilerPC, c.FullyQualifiedName, c.Name.Substring(0, c.Name.Length - 5).ToLowerInvariant()));
						}
					}
				}
			}
			
			List<string> knownVariableNames = new List<string>();
			foreach (object o in result) {
				IMember m = o as IMember;
				if (m != null) {
					knownVariableNames.Add(m.Name);
				}
			}
			VariableListLookupVisitor vllv = new VariableListLookupVisitor(knownVariableNames, this);
			vllv.Visit(GetCurrentBooMethod());
			foreach (KeyValuePair<string, IReturnType> entry in vllv.Results) {
				result.Add(new DefaultField.LocalVariableField(entry.Value, entry.Key, DomRegion.Empty, callingClass));
			}
			
			return result;
		}
		
		// used by ctrl+space and resolve visitor (resolve identifier)
		public ArrayList GetImportedNamespaceContents()
		{
			ArrayList result = new ArrayList();
			AddImportedNamespaceContents(result);
			return result;
		}
		
		void AddImportedNamespaceContents(ArrayList list)
		{
			IClass c;
			foreach (KeyValuePair<string, string> pair in BooAmbience.TypeConversionTable) {
				c = GetPrimitiveClass(pc, pair.Key, pair.Value);
				if (c != null) list.Add(c);
			}
			NRResolver.AddImportedNamespaceContents(list, cu, callingClass);
		}
		#endregion
	}
}
