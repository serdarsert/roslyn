﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection.Metadata
Imports System.Threading
Imports Microsoft.CodeAnalysis.ExpressionEvaluator
Imports Microsoft.CodeAnalysis.VisualBasic.ExpressionEvaluator
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.VisualStudio.SymReaderInterop
Imports Roslyn.Test.Utilities
Imports Xunit

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests
    Public Class DteeTests
        Inherits ExpressionCompilerTestBase

        Private Const DteeEntryPointSource = "
Imports System.Collections

Class HostProc
    Sub BreakForDebugger()
    End Sub
End Class
"
        Private Const DteeEntryPointName = "HostProc.BreakForDebugger"

        <Fact>
        Public Sub IsDteeEntryPoint()
            Const source = "
Class HostProc
    Sub BreakForDebugger()
    End Sub
End Class

Class AppDomain
    Sub ExecuteAssembly()
    End Sub
End Class
"
            Dim comp = CreateCompilationWithMscorlib({source})
            Dim [global] = comp.GlobalNamespace
            Dim m1 = [global].GetMember(Of NamedTypeSymbol)("HostProc").GetMember(Of MethodSymbol)("BreakForDebugger")
            Dim m2 = [global].GetMember(Of NamedTypeSymbol)("AppDomain").GetMember(Of MethodSymbol)("ExecuteAssembly")
            Assert.True(EvaluationContext.IsDteeEntryPoint(m1))
            Assert.True(EvaluationContext.IsDteeEntryPoint(m2))
        End Sub

        <Fact>
        Public Sub IsDteeEntryPoint_Namespace()
            Const source = "
Namespace N
    Class HostProc
        Sub BreakForDebugger()
        End Sub
    End Class

    Class AppDomain
        Sub ExecuteAssembly()
        End Sub
    End Class
End Namespace
"
            Dim comp = CreateCompilationWithMscorlib({source})
            Dim [namespace] = comp.GlobalNamespace.GetMember(Of NamespaceSymbol)("N")
            Dim m1 = [namespace].GetMember(Of NamedTypeSymbol)("HostProc").GetMember(Of MethodSymbol)("BreakForDebugger")
            Dim m2 = [namespace].GetMember(Of NamedTypeSymbol)("AppDomain").GetMember(Of MethodSymbol)("ExecuteAssembly")
            Assert.True(EvaluationContext.IsDteeEntryPoint(m1))
            Assert.True(EvaluationContext.IsDteeEntryPoint(m2))
        End Sub

        <Fact>
        Public Sub IsDteeEntryPoint_CaseSensitive()
            Const source = "
Namespace N1
    Class HostProc
        Sub breakfordebugger()
        End Sub
    End Class

    Class AppDomain
        Sub executeassembly()
        End Sub
    End Class
End Namespace

Namespace N2
    Class hostproc
        Sub BreakForDebugger()
        End Sub
    End Class

    Class appdomain
        Sub ExecuteAssembly()
        End Sub
    End Class
End Namespace
"
            Dim comp = CreateCompilationWithMscorlib({source})
            Dim [global] = comp.GlobalNamespace

            Dim [namespace] = [global].GetMember(Of NamespaceSymbol)("N1")
            Dim m1 = [namespace].GetMember(Of NamedTypeSymbol)("HostProc").GetMember(Of MethodSymbol)("BreakForDebugger")
            Dim m2 = [namespace].GetMember(Of NamedTypeSymbol)("AppDomain").GetMember(Of MethodSymbol)("ExecuteAssembly")
            Assert.False(EvaluationContext.IsDteeEntryPoint(m1))
            Assert.False(EvaluationContext.IsDteeEntryPoint(m2))

            [namespace] = [global].GetMember(Of NamespaceSymbol)("N2")
            m1 = [namespace].GetMember(Of NamedTypeSymbol)("HostProc").GetMember(Of MethodSymbol)("BreakForDebugger")
            m2 = [namespace].GetMember(Of NamedTypeSymbol)("AppDomain").GetMember(Of MethodSymbol)("ExecuteAssembly")
            Assert.False(EvaluationContext.IsDteeEntryPoint(m1))
            Assert.False(EvaluationContext.IsDteeEntryPoint(m2))
        End Sub

        <Fact>
        Public Sub DteeEntryPointImportsIgnored()
            Dim comp = CreateCompilationWithMscorlib({DteeEntryPointSource}, compOptions:=TestOptions.DebugDll, assemblyName:=GetUniqueName())
            Dim compModuleInstance = GetModuleInstance(comp)
            Dim corlibModuleReference = MscorlibRef.ToModuleInstance(Nothing, Nothing)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(compModuleInstance, corlibModuleReference))
            Dim lazyAssemblyReaders = MakeLazyAssemblyReaders(runtimeInstance)

            Dim evalContext = CreateMethodContext(runtimeInstance, DteeEntryPointName, lazyAssemblyReaders:=lazyAssemblyReaders)
            Dim compContext = evalContext.CreateCompilationContext(MakeDummySyntax())

            Dim rootNamespace As NamespaceSymbol = Nothing
            Dim currentNamespace As NamespaceSymbol = Nothing
            Dim typesAndNamespaces As ImmutableArray(Of NamespaceOrTypeAndImportsClausePosition) = Nothing
            Dim aliases As Dictionary(Of String, AliasAndImportsClausePosition) = Nothing
            Dim xmlNamespaces As Dictionary(Of String, XmlNamespaceAndImportsClausePosition) = Nothing
            ImportsDebugInfoTests.GetImports(compContext, rootNamespace, currentNamespace, typesAndNamespaces, aliases, xmlNamespaces)

            Assert.Equal("", rootNamespace.Name)
            Assert.Equal("", currentNamespace.Name)
            Assert.True(typesAndNamespaces.IsDefault)
            Assert.Null(aliases)
            Assert.Null(xmlNamespaces)
        End Sub

        <Fact>
        Public Sub ImportStrings_DefaultNamespaces()
            Dim source1 = "
Class C1
    Sub M() ' Need a method to which we can attach import custom debug info.
    End Sub
End Class
"

            Dim source2 = "
Class C2
    Sub M() ' Need a method to which we can attach import custom debug info.
    End Sub
End Class
"
            Dim comp1 = CreateCompilationWithMscorlib({source1}, compOptions:=TestOptions.DebugDll.WithRootNamespace("root1"), assemblyName:=GetUniqueName())
            Dim compModuleInstance1 = GetModuleInstance(comp1)

            Dim comp2 = CreateCompilationWithMscorlib({source2}, compOptions:=TestOptions.DebugDll.WithRootNamespace("root2"), assemblyName:=GetUniqueName())
            Dim compModuleInstance2 = GetModuleInstance(comp2)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                compModuleInstance1,
                compModuleInstance2,
                MscorlibRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo, "root1", "root2")
        End Sub

        <Fact>
        Public Sub ImportStrings_ModuleNamespaces()
            Dim source1 = "
Namespace N1
    Module M
    End Module
End Namespace

Namespace N2
    Namespace N3
        Module M
        End Module
    End Namespace
End Namespace

Namespace N4
    Class C
    End Class
End Namespace
"

            Dim source2 = "
Namespace N1 ' Also imported for source1
    Module M2
    End Module
End Namespace

Namespace N5
    Namespace N6
        Module M
        End Module
    End Namespace
End Namespace

Namespace N7
    Class C
    End Class
End Namespace
"
            Dim comp1 = CreateCompilationWithMscorlib({source1}, {MsvbRef}, compOptions:=TestOptions.DebugDll, assemblyName:=GetUniqueName())
            Dim compModuleInstance1 = GetModuleInstance(comp1)

            Dim comp2 = CreateCompilationWithMscorlib({source2}, {MsvbRef}, compOptions:=TestOptions.DebugDll, assemblyName:=GetUniqueName())
            Dim compModuleInstance2 = GetModuleInstance(comp2)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                compModuleInstance1,
                compModuleInstance2,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo, "N1", "N2.N3", "N5.N6")
        End Sub

        <Fact>
        Public Sub ImportStrings_NoMethods()
            Dim comp = CreateCompilationWithMscorlib({""}, {MsvbRef}, compOptions:=TestOptions.DebugDll.WithRootNamespace("root"), assemblyName:=GetUniqueName())
            Dim compModuleInstance = GetModuleInstance(comp)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                compModuleInstance,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            ' Since there are no methods in the assembly, there is no import custom debug info, so we
            ' have no way to find the root namespace.
            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo)
        End Sub

        <Fact>
        Public Sub ImportStrings_IgnoreAssemblyWithoutPdb()
            Dim source1 = "
Namespace N1
    Module M
    End Module
End Namespace
"

            Dim source2 = "
Namespace N2
    Module M
    End Module
End Namespace
"
            Dim comp1 = CreateCompilationWithMscorlib({source1}, {MsvbRef}, compOptions:=TestOptions.ReleaseDll, assemblyName:=GetUniqueName())
            Dim compModuleInstance1 = GetModuleInstance(comp1)

            Dim comp2 = CreateCompilationWithMscorlib({source2}, {MsvbRef}, compOptions:=TestOptions.DebugDll, assemblyName:=GetUniqueName())
            Dim compModuleInstance2 = GetModuleInstance(comp2)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                compModuleInstance1,
                compModuleInstance2,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo, "N2")
        End Sub

        <Fact>
        Public Sub FalseModule_Nested()
            ' NOTE: VB only allows top-level module types.
            Dim ilSource = "
.assembly 'IL' {} 

.assembly extern mscorlib 
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)
  .ver 4:0:0:0
} 

.assembly extern Microsoft.VisualBasic { } 

.class public auto ansi N1.Outer
       extends [mscorlib]System.Object
{
  .class auto ansi nested public sealed Inner
         extends [mscorlib]System.Object
  {
    .custom instance void [Microsoft.VisualBasic]Microsoft.VisualBasic.CompilerServices.StandardModuleAttribute::.ctor()
             = {}

    .method public specialname rtspecialname 
            instance void  .ctor() cil managed
    {
      ldarg.0
      call       instance void [mscorlib]System.Object::.ctor()
      ret
    }
  } // end of class Inner

  .method public specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    ldarg.0
    call       instance void [mscorlib]System.Object::.ctor()
    ret
  }
} // end of class N1.Outer
"
            Dim ilModuleInstance = GetModuleInstanceForIL(ilSource)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                ilModuleInstance,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo)
        End Sub

        <Fact>
        Public Sub FalseModule_Generic()
            ' NOTE: VB only allows non-generic module types.
            Dim ilSource = "
.assembly 'IL' {} 

.assembly extern mscorlib 
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)
  .ver 4:0:0:0
} 

.assembly extern Microsoft.VisualBasic { } 

.class public auto ansi sealed N1.M`1<T>
       extends [mscorlib]System.Object
{
  .custom instance void [Microsoft.VisualBasic]Microsoft.VisualBasic.CompilerServices.StandardModuleAttribute::.ctor()
           = {}

} // end of class M
"
            Dim ilModuleInstance = GetModuleInstanceForIL(ilSource)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                ilModuleInstance,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo)
        End Sub

        <Fact>
        Public Sub FalseModule_Interface()
            ' NOTE: VB only allows non-interface module types.
            Dim ilSource = "
.assembly 'IL' {} 

.assembly extern mscorlib 
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)
  .ver 4:0:0:0
} 

.assembly extern Microsoft.VisualBasic { } 

.class interface private abstract auto ansi I
{
  .custom instance void [Microsoft.VisualBasic]Microsoft.VisualBasic.CompilerServices.StandardModuleAttribute::.ctor()
           = {}

} // end of class I
"
            Dim ilModuleInstance = GetModuleInstanceForIL(ilSource)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                ilModuleInstance,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))

            Dim methodDebugInfo = EvaluationContext.SynthesizeMethodDebugInfoForDtee(MakeAssemblyReaders(runtimeInstance))
            CheckDteeMethodDebugInfo(methodDebugInfo)
        End Sub

        <Fact>
        Public Sub ImportSymbols()
            Dim source1 = "
Namespace N1
    Module M
        Sub M() ' Need a method to record the root namespace.
        End Sub
    End Module
End Namespace

Namespace N2
    Module M
    End Module
End Namespace
"

            Dim source2 = "
Namespace N1 ' Also imported for source1
    Module M2
        Sub M() ' Need a method to record the root namespace.
        End Sub
    End Module
End Namespace

Namespace N3
    Module M
    End Module
End Namespace
"

            Dim dteeComp = CreateCompilationWithMscorlib({DteeEntryPointSource}, compOptions:=TestOptions.DebugDll, assemblyName:=GetUniqueName())
            Dim dteeModuleInstance = GetModuleInstance(dteeComp)

            Dim comp1 = CreateCompilationWithMscorlib({source1}, {MsvbRef}, compOptions:=TestOptions.DebugDll.WithRootNamespace("root"), assemblyName:=GetUniqueName())
            Dim compModuleInstance1 = GetModuleInstance(comp1)

            Dim comp2 = CreateCompilationWithMscorlib({source2}, {MsvbRef}, compOptions:=TestOptions.DebugDll.WithRootNamespace("root"), assemblyName:=GetUniqueName())
            Dim compModuleInstance2 = GetModuleInstance(comp2)

            Dim runtimeInstance = CreateRuntimeInstance(ImmutableArray.Create(
                dteeModuleInstance,
                compModuleInstance1,
                compModuleInstance2,
                MscorlibRef.ToModuleInstance(Nothing, Nothing),
                MsvbRef.ToModuleInstance(Nothing, Nothing)))
            Dim lazyAssemblyReaders = MakeLazyAssemblyReaders(runtimeInstance)

            Dim evalContext = CreateMethodContext(runtimeInstance, DteeEntryPointName, lazyAssemblyReaders:=lazyAssemblyReaders)
            Dim compContext = evalContext.CreateCompilationContext(MakeDummySyntax())

            Dim rootNamespace As NamespaceSymbol = Nothing
            Dim currentNamespace As NamespaceSymbol = Nothing
            Dim typesAndNamespaces As ImmutableArray(Of NamespaceOrTypeAndImportsClausePosition) = Nothing
            Dim aliases As Dictionary(Of String, AliasAndImportsClausePosition) = Nothing
            Dim xmlNamespaces As Dictionary(Of String, XmlNamespaceAndImportsClausePosition) = Nothing
            ImportsDebugInfoTests.GetImports(compContext, rootNamespace, currentNamespace, typesAndNamespaces, aliases, xmlNamespaces)

            Assert.Equal("", rootNamespace.Name)
            Assert.Equal("", currentNamespace.Name)
            Assert.Null(aliases)
            Assert.Null(xmlNamespaces)
            AssertEx.SetEqual(typesAndNamespaces.Select(Function(tn) tn.NamespaceOrType.ToTestDisplayString()), "root", "root.N1", "root.N2", "root.N3")
        End Sub

        Private Shared Function MakeDummySyntax() As Syntax.ExecutableStatementSyntax
            Return VisualBasicSyntaxTree.ParseText("?Nothing").GetRoot().DescendantNodes().OfType(Of Syntax.ExecutableStatementSyntax)().Single()
        End Function

        Private Shared Function MakeLazyAssemblyReaders(runtimeInstance As RuntimeInstance) As Lazy(Of ImmutableArray(Of AssemblyReaders))
            Return New Lazy(Of ImmutableArray(Of AssemblyReaders))(
                            Function() MakeAssemblyReaders(runtimeInstance),
                            LazyThreadSafetyMode.None)
        End Function

        Private Shared Function MakeAssemblyReaders(runtimeInstance As RuntimeInstance) As ImmutableArray(Of AssemblyReaders)
            Return ImmutableArray.CreateRange(runtimeInstance.Modules.
                Where(Function(instance) instance.SymReader IsNot Nothing).
                Select(Function(instance) New AssemblyReaders(instance.MetadataReader, instance.SymReader)))
        End Function

        Private Shared Function GetModuleInstance(comp As VisualBasicCompilation) As ModuleInstance
            Dim makePdb = comp.Options.OptimizationLevel = OptimizationLevel.Debug

            Dim peBytes As Byte()
            Dim pdbBytes As Byte()
            Using pdbStream = If(makePdb, New MemoryStream(), Nothing)
                Using peStream As New MemoryStream()
                    Dim emitResult = comp.Emit(peStream, pdbStream)
                    AssertNoErrors(emitResult.Diagnostics)
                    Assert.True(emitResult.Success)

                    peBytes = peStream.ToArray()
                    pdbBytes = pdbStream?.ToArray()
                End Using
            End Using
            Assert.Equal(makePdb, pdbBytes IsNot Nothing)
            Dim compRef = AssemblyMetadata.CreateFromImage(peBytes).GetReference()
            Return compRef.ToModuleInstance(peBytes, If(makePdb, New SymReader(pdbBytes), Nothing))
        End Function

        Private Shared Function GetModuleInstanceForIL(ilSource As String) As ModuleInstance
            Dim peBytes As ImmutableArray(Of Byte) = Nothing
            Dim pdbBytes As ImmutableArray(Of Byte) = Nothing
            EmitILToArray(ilSource, appendDefaultHeader:=False, includePdb:=True, assemblyBytes:=peBytes, pdbBytes:=pdbBytes)
            Dim compRef = AssemblyMetadata.CreateFromImage(peBytes).GetReference()
            Return compRef.ToModuleInstance(peBytes.ToArray(), New SymReader(pdbBytes.ToArray()))
        End Function

        Private Shared Sub CheckDteeMethodDebugInfo(methodDebugInfo As MethodDebugInfo, ParamArray namespaceNames As String())
            Assert.Equal("", methodDebugInfo.DefaultNamespaceName)

            Dim importRecordGroups = methodDebugInfo.ImportRecordGroups
            Assert.Equal(2, importRecordGroups.Length)
            Dim projectLevelImportRecords As ImmutableArray(Of ImportRecord) = importRecordGroups(0)
            Dim fileLevelImportRecords As ImmutableArray(Of ImportRecord) = importRecordGroups(1)

            Assert.Empty(fileLevelImportRecords)

            AssertEx.All(projectLevelImportRecords, Function(record) TypeOf record Is NativeImportRecord)
            AssertEx.All(projectLevelImportRecords, Function(record) DirectCast(record, NativeImportRecord).ExternAlias Is Nothing)
            AssertEx.All(projectLevelImportRecords, Function(record) record.TargetKind = ImportTargetKind.Namespace)
            AssertEx.All(projectLevelImportRecords, Function(record) record.Alias Is Nothing)
            AssertEx.SetEqual(projectLevelImportRecords.Select(Function(record) record.TargetString), namespaceNames)
        End Sub
    End Class
End Namespace