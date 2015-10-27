﻿namespace FSharpVSPowerTools.QuickInfo

open System
open System.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Shell.Interop
open FSharpVSPowerTools
open FSharpVSPowerTools.ProjectSystem
open FSharpVSPowerTools.AsyncMaybe
open FSharp.ViewModule
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices

[<AutoOpen>]
module private Extensions =
    type FSharpErrorSeverity with
        member x.ToString () =
            match x with
            | FSharpErrorSeverity.Warning -> "Warning"
            | FSharpErrorSeverity.Error -> "Error"

type QuickInfoVisual = FsXaml.XAML<"QuickInfoMargin.xaml", ExposeNamedProperties=true>

type QuickInfoViewModel() as self = 
    inherit ViewModelBase()
    let quickInfo = self.Factory.Backing(<@@ self.QuickInfo @@>, "")
    member __.QuickInfo
        with get () = quickInfo.Value
        and set v = quickInfo.Value <- v

type QuickInfoMargin (textDocument: ITextDocument,
                      view: ITextView, 
                      vsLanguageService: VSLanguageService, 
                      serviceProvider: IServiceProvider,
                      projectFactory: ProjectFactory) = 

    let updateLock = obj()
    let model = QuickInfoViewModel()
    let visual = QuickInfoVisual()

    do visual.Root.DataContext <- model
       visual.tbQuickInfo.MouseDoubleClick.Add (fun _ ->
           System.Windows.Clipboard.SetText visual.tbQuickInfo.Text
           visual.tbQuickInfo.SelectAll())
    
    let buffer = view.TextBuffer

    let updateQuickInfo (tooltip: string option, errors: ((FSharpErrorSeverity * string list) []) option) =
        let updateFunc () =    
            // helper function to lead a string builder across the collection of 
            // errors accumulating lines annotated with their index number   
            let errorString (errors:string list) (sb:StringBuilder) =
                match errors with 
                | [e] -> sb.Append e
                | _ -> (sb, errors ) ||> List.foldi (fun sb i e -> 
                    sb.Append(sprintf "%d. %s" (i + 1) e).Append(" "))
                       
            let currentInfo =
                // if the tooltip contains errors show them
                errors |> Option.map (fun errors ->
                    (StringBuilder (), errors) ||> Array.fold (fun sb (severity, err) -> 
                        let errorls = List.map String.trim err
                        let title = 
                            match errorls with
                            | [_] -> sprintf "%s" <| string severity
                            | _ -> sprintf "%s (%d)" (string severity) errorls.Length
                        (sb.Append(title).Append (": ") |> errorString errorls).Append (" ")) |> string)  
                // show type info if there aren't any errors
                |> Option.orElse (tooltip |> Option.bind (fun tooltip ->
                    tooltip  |> String.firstNonEmptyLine |> Option.map (fun str ->
                        if str.StartsWith ("type ", StringComparison.Ordinal) then
                            let index = str.LastIndexOf ("=", StringComparison.Ordinal)
                            if index > 0 then str.[0..index-1] else str
                        else str)))
                // if there are no results the panel will be empty
                |> Option.getOrElse ""

            model.QuickInfo <- currentInfo
        lock updateLock updateFunc      
        
    // helper function in the form required by mapNonEmptyLines
    let flattener (sb:StringBuilder) (str:string) : StringBuilder =
            if str.Length > 0 && Char.IsUpper str.[0] then (sb.Append ". ").Append (String.trim str) 
            else (sb.Append " ").Append (String.trim str)
                

    let flattenLines (x: string) : string =
        let appendDot (x:string) = 
            if x.Length > 0 && x.[x.Length - 1] <> '.' then x + "." else x
        match x with
        | null -> ""
        | x -> x |> String.mapNonEmptyLines flattener |> appendDot

    let updateAtCaretPosition () =
        let caretPos = view.Caret.Position
        match buffer.GetSnapshotPoint caretPos with
        | Some point ->
            let project =
                maybe {
                    let dte = serviceProvider.GetService<EnvDTE.DTE, SDTE>()
                    let! doc = dte.GetCurrentDocument (textDocument.FilePath)
                    let! project = projectFactory.CreateForDocument buffer doc
                    return project }
            asyncMaybe {
                let! project = project
                let! tooltip =
                    asyncMaybe {
                        let! newWord, longIdent = vsLanguageService.GetLongIdentSymbol (point, project)
                        let lineStr = point.GetContainingLine().GetText()
                        let idents = String.split StringSplitOptions.None [|"."|] longIdent.Text |> Array.toList
                        let! (FSharpToolTipText tooltip) =
                            vsLanguageService.GetOpenDeclarationTooltip(
                                longIdent.Line + 1, longIdent.RightColumn, lineStr, idents, project, 
                                textDocument.FilePath, newWord.Snapshot.GetText())
                        return!
                            tooltip
                            |> List.tryHead
                            |> Option.bind (function
                                | FSharpToolTipElement.Single (s, _) -> Some s
                                | FSharpToolTipElement.Group ((s, _) :: _) -> Some s
                                | _ -> None)
                    } |> Async.map Some
                let! checkResults = 
                    vsLanguageService.ParseAndCheckFileInProject(textDocument.FilePath, buffer.CurrentSnapshot.GetText(), project) |> liftAsync
                let! errors =
                    asyncMaybe {
                        let! errors = checkResults.GetErrors()
                        do! (if Array.isEmpty errors then None else Some())
                        return!
                            seq { for e in errors do
                                    if String.Equals(textDocument.FilePath, e.FileName, StringComparison.InvariantCultureIgnoreCase) then
                                        match fromRange buffer.CurrentSnapshot (e.StartLineAlternate, e.StartColumn, e.EndLineAlternate, e.EndColumn) with
                                        | Some span when point.InSpan span -> yield e.Severity, flattenLines e.Message
                                        | _ -> () }
                            |> Seq.groupBy fst
                            |> Seq.sortBy (fun (severity, _) -> if severity = FSharpErrorSeverity.Error then 0 else 1)
                            |> Seq.map (fun (s, es) -> s, es |> Seq.map snd |> Seq.distinct |> List.ofSeq)
                            |> Seq.toArray
                            |> function [||] -> None | es -> Some es
                    } |> Async.map Some
                return tooltip, errors
            } 
            |> Async.map (Option.getOrElse (None, None) >> updateQuickInfo)
            |> Async.StartInThreadPoolSafe
        | None -> updateQuickInfo (None, None)

    let docEventListener = new DocumentEventListener ([ViewChange.layoutEvent view; ViewChange.caretEvent view], 200us, updateAtCaretPosition)

    interface IWpfTextViewMargin with
        member __.VisualElement = upcast visual
        member __.MarginSize = visual.ActualHeight + 2.
        member __.Enabled = true
        
        member x.GetTextViewMargin name =
            match name with
            | Constants.QuickInfoMargin -> upcast x
            | _ -> Unchecked.defaultof<_>

    interface IDisposable with
        member __.Dispose() = (docEventListener :> IDisposable).Dispose()