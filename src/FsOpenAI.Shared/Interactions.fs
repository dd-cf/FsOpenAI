﻿namespace FsOpenAI.Shared.Interactions
open System
open FsOpenAI.Shared
open FSharp.Reflection
open FsOpenAI.Shared.Interactions.Core.Interactions

module Interaction =
    let newUserMessage cntnt = {MsgId = Utils.newId(); Role=MessageRole.User; Message=cntnt}
    let newAsstantMessage cntnt =  {MsgId = Utils.newId(); Role=MessageRole.Assistant QueriedDocuments.Empty; Message=cntnt}

    let getText c =
        let sb = System.Text.StringBuilder()
        sb.Append("[System]").AppendLine().AppendLine(c.SystemMessage) |> ignore
        c.Messages
        |> List.iter (fun m ->
            let tag = if m.IsUser then "[User]" else "[Assistant]"
            sb.Append(tag).AppendLine().AppendLine(m.Message) |> ignore)
        sb.ToString()

    let genName name msg question =
        match Utils.isEmpty msg, Utils.isEmpty question with
        | true,true   -> name
        | true,false  -> Utils.shorten 18 question
        | _,_         -> Utils.shorten 18 msg

    let tag (ch:Interaction) =
        let cType =
            match ch.Mode with
            | M_Plain           -> "Chat"
            | M_Doc             -> "Doc."
            | M_Index           -> "Q&A"
            | M_Doc_Index       -> "Doc.+ "
            | M_CodeEval        -> "Wholesale"
        $"{cType} [{ch.Parameters.Backend}] ..."

    let systemMessage (c:Interaction) = c.SystemMessage

    let messages (c:Interaction) = c.Messages

    let cBag c =
        c.Types
        |> List.tryPick (function Plain bag -> Some bag | _ -> None)
        |> Option.defaultWith (fun _ -> failwith "unexpected chat type")

    let configuredChatBackends (cfg:AppConfig) =
        (cfg.ModelsConfig.LongChatModels |> List.map _.Backend)
        @ (cfg.ModelsConfig.ShortChatModels |> List.map _.Backend)
        |> List.distinct

    let getExplorationModeCases() = FSharpType.GetUnionCases typeof<ExplorationMode>
    let getExplorationModeCase (mode:ExplorationMode) = FSharpValue.GetUnionFields(mode,typeof<ExplorationMode>)

    let getSearchModeCases() = FSharpType.GetUnionCases typeof<SearchMode>
    let getSearchModeCase (mode:SearchMode) = FSharpValue.GetUnionFields(mode,typeof<SearchMode>)

    let maxDocs defaultVal (ch:Interaction) = 
        ch.Types
        |> List.tryPick (function IndexQnA bag -> Some bag.MaxDocs | _ -> None)
        |> Option.defaultValue defaultVal

    let lastSearchQuery (ch:Interaction) =
        List.rev ch.Messages
        |> List.map (_.Role)
        |> List.tryPick (function Assistant x -> x.SearchQuery | _ -> None)

    let lastNonEmptyUserMessageText (ch:Interaction) =
        let msg = ch.Messages |> List.rev |> List.tryFind(fun x->x.IsUser && Utils.notEmpty x.Message)
        match msg with
        | Some msg -> msg.Message
        | None     -> ""

    let label (ch:Interaction) =
        match ch.Name with
        | Some n -> n
        | None   -> genName (tag ch) (lastNonEmptyUserMessageText ch) ch.Question


    let docContent (ch:Interaction) =
        ch.Types
        |> List.tryPick (function 
            | QnADoc dc -> Some dc 
            | _ -> None)


    let qaBag ch =
        ch.Types
        |> List.tryPick (function 
            | IndexQnA bag -> Some bag 
            | _ -> None)

    let getIndexes ch =
        ch.Types
        |> List.collect (function 
            | IndexQnA bag      -> bag.Indexes
            | _                 -> [])

    let setQABag bag ch =
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | IndexQnA _          -> IndexQnA bag
                    | x                   -> x)
        }

    let setDocContent dc ch =
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | QnADoc _         -> QnADoc dc
                    | x                -> x)
        }


    let addIndex idx ch =
        let updateBag (bag:QABag) = {bag with Indexes = bag.Indexes @ [idx]}
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | IndexQnA bag -> IndexQnA (updateBag bag) 
                    | x -> x)
    }

    let setIndexes idxs ch =
        let updateBag (bag:QABag) = {bag with Indexes = idxs}
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | IndexQnA bag -> IndexQnA (updateBag bag) 
                    | x -> x)
        }

    let clearDocuments c =
        {c with
            Messages =
                c.Messages
                |> List.map(fun m ->
                    match m.Role with
                    | Assistant r -> {m with Role = Assistant QueriedDocuments.Empty}
                    | _           -> m)
        }

    let addDocuments docs c =
        let h,tail = match List.rev c.Messages with h::t -> h,t | _ -> failwith "no messages in chat"
        let h =
            match h.Role with
            | Assistant d -> {h with Role = Assistant {d with Docs=docs}}
            | _ -> failwith "Expected an Assistant message"
        let msgs = h::tail
        let _,msgs =
            ((0,[]),msgs)
            ||> List.fold (fun (count,acc) m ->
                let count',m' =
                    match m.Role with
                    | Assistant _ when count > C.MAX_DOCLISTS_PER_CHAT -> count+1,{m with Role = Assistant QueriedDocuments.Empty}
                    | Assistant _ -> count+1,m
                    | _ -> count,m
                count',m'::acc)
        {c with Messages = msgs}

    let addDelta delta c =
        let h,tail = match List.rev c.Messages with h::t -> h,t | _ -> failwith "no messages in chat"
        let h = {h with Message = h.Message+delta}
        {c with Messages = List.rev (h::tail)}

    let setSystemMessage msg (c:Interaction) = {c with SystemMessage = msg}

    let setQuestion q (c:Interaction) = {c with Question = q}

    let setMode mode ch = {ch with Parameters = {ch.Parameters with Mode = mode}}

    let setMaxDocs maxDocs ch =
        let updateBag (bag:QABag) = {bag with MaxDocs=maxDocs}
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | IndexQnA bag -> IndexQnA (updateBag bag) 
                    | x -> x)
        }

    let endBuffering errOccured c  =
        let c = {c with IsBuffering=false}
        if errOccured then
            let msgs = List.rev c.Messages
            match msgs with
            | t1::rest when t1.IsUser    -> {c with Messages = List.rev rest; Question=t1.Message.Trim()}
            | _::t1::rest when t1.IsUser -> {c with Messages = List.rev rest; Question=t1.Message.Trim()}
            | _ -> failwith "unexpected chat state"
        else
            c

    ///Set the given text as the last user message text in the chat.
    ///Either update the existng user message text or add a new user message record, if needed
    ///Ignore an assistant message that is empty, if its the last message
    let setUserMessage msg c =
        let msgs = List.rev c.Messages
        let msgs' =
            match msgs with
            | []                        -> [newUserMessage msg]
            | t1::t2::rest when not(t1.IsUser)
                                && t2.IsUser &&
                                Utils.isEmpty t1.Message
                                        -> t1::{t2 with Message=msg}::rest
            | t1::rest when t1.IsUser   -> {t1 with Message=msg}::rest
            | xs                        -> (newUserMessage msg)::xs
        {c with Messages = List.rev msgs'}

    let restartFromMsg (msg:InteractionMessage) (c:Interaction) =
        if c.IsBuffering then
            c
        elif not msg.IsUser then failwith "cannot restart chat from non-user message"
        else
            let msgs = c.Messages |> List.takeWhile (fun msg' -> msg<>msg')
            {c with Messages = msgs; Question = msg.Message }

    //clear chat messages and set the given prompt as the question
    let clearChat prompt (c:Interaction) = {c with Messages = []; Question=prompt}

    let private setContents (dc:DocumentContent) (text,isDone) =
         let cnts = match dc.DocumentText with Some t -> String.Join("\r",[t;text]) | None -> text
         let status = if isDone then Ready else dc.Status
         {dc with DocumentText = Some cnts; Status = status}

    let setFileContents (text,isDone) (ch:Interaction) =
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | QnADoc dc -> QnADoc (setContents dc (text,isDone))
                    | x -> x)
        }

    let setDocumentStatus status (ch:Interaction) =
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | QnADoc dc -> QnADoc {dc with Status = status}
                    | x -> x)
        }


    let setParameters parms (ch:Interaction) = {ch with Parameters = parms}

    let defaultParameters backend interactionType =
        {InteractionParameters.Default with
            Backend = backend
            Mode =
                match interactionType with
                | IndexQnA _ | QnADoc _ -> ExplorationMode.Factual
                | _     -> InteractionParameters.Default.Mode
        }

    ///trim UI state that is not required for processing chat (cannot be serialized)
    let removeUIState ch = 
        {ch with 
            Types = 
                ch.Types 
                |> List.map (function 
                    | QnADoc dc  -> QnADoc {dc with DocumentRef = None; DocType=None}
                    | x -> x)
            Feedback = None
        }

    let keepMessages n ch = {ch with Messages = ch.Messages |> List.rev |> List.truncate n |> List.rev}

    let removeReferencesText ch =
        {ch with
            Messages =
                ch.Messages
                |> List.map (fun m ->
                    match m.Role with
                    | Assistant q ->
                        {m with
                            Role = Assistant {
                                                SearchQuery = None
                                                Docs = q.Docs |> List.map(fun d -> {d with Embedding=[||]; Text="[...]"})
                                              }
                        }
                    | _ -> m
                )
        }

    let removeDocumentText ch =
        {ch with Types = 
                    ch.Types 
                    |> List.map (function 
                        | QnADoc dc  -> QnADoc {dc with DocumentText = None; Status = No_Document}
                        | x -> x)
        }

    let sessionSerialize ch =
        {ch with Notifications=[]; IsBuffering=false}
        |> removeUIState
        |> keepMessages 4
        |> removeDocumentText
        |> removeReferencesText

    //trim state that is not required for processing chat
    let preSerialize ch =
        {ch with Notifications=[]; IsBuffering=false}
        |> removeUIState
        |> clearDocuments

    let create ctype backend msg =
        let iType,mode =
            match ctype with
            | Crt_Plain           -> InteractionType.Plain ChatBag.Default, M_Plain
            | Crt_IndexQnA        -> InteractionType.IndexQnA QABag.Default, M_Index
            | Crt_QnADoc          -> InteractionType.QnADoc DocumentContent.Default, M_Doc
        let msg = defaultArg msg ""
        let id = Utils.newId()
        let c =
            {
                Id = id
                Name = None
                Feedback = None
                Mode = mode
                Types = [iType]
                SystemMessage = C.defaultSystemMessage
                Question = msg
                Messages = []
                Parameters = defaultParameters backend iType
                Timestamp = DateTime.Now
                IsBuffering = false
                Notifications = []
            }
        c.Id,c

    let setUseWeb useWeb c =
        {c with Types = 
                    c.Types 
                    |> List.map (function 
                        | Plain cbag -> Plain {cbag with UseWeb=useWeb}
                        | x -> x)
        }

    let setFeedback feedback c = {c with Feedback = feedback}

module Interactions =

    let empty = []

    let addNew backend ctype msg cs =
        let id,c = Interaction.create ctype backend msg
        id, cs @ [c]

    let docContent id cs = cs |> List.find(fun c -> c.Id = id) |> Interaction.docContent

    let remove id cs = cs |> List.filter(fun c -> c.Id <> id)

    let setQABag id bag cs = updateWith (Interaction.setQABag bag) id cs

    let setDocContent id dc cs = updateWith (Interaction.setDocContent dc) id cs

    let setUserMessage id msg cs = updateWith (Interaction.setUserMessage msg) id cs

    let setLastUserMessage id msg cs = updateWith (Interaction.setUserMessage msg) id cs

    let addMessage id msg cs = updateWith (fun c -> {c with Messages = c.Messages @ [msg]}) id cs

    let clearChat id prompt cs = updateWith (Interaction.clearChat prompt) id cs

    let restartFromMsg id msg cs =  updateWith (Interaction.restartFromMsg msg) id cs

    let setSystemMessage id sysMsg cs = updateWith (Interaction.setSystemMessage sysMsg) id cs

    let setName id name cs = updateWith (fun c -> {c with Name=name}) id cs

    let setParms id parms cs = updateWith (fun c -> {c with Parameters=parms}) id cs

    let addDelta id delta cs = updateWith (Interaction.addDelta delta) id cs

    let startBuffering id cs = updateWith (fun c -> {c with IsBuffering=true}) id cs

    let endBuffering id errorOccured cs = updateWith (Interaction.endBuffering errorOccured) id cs

    let addNotification id note cs = updateWith (fun c -> {c with Notifications=c.Notifications @ [note]}) id cs

    let clearNotifications id cs = updateWith (fun c -> {c with Notifications=[]}) id cs

    let clearDocuments id cs = updateWith Interaction.clearDocuments id cs

    let setFileContents id (text,isDone) cs = updateWith (Interaction.setFileContents (text,isDone)) id cs

    let setDocumentStatus id status cs = updateWith (Interaction.setDocumentStatus status) id cs

    let setUseWeb id useWeb cs = updateWith (Interaction.setUseWeb useWeb) id cs

    let setQuestion id q cs = updateWith (Interaction.setQuestion q) id cs

    let addDocuments id docs cs = updateWith (Interaction.addDocuments docs) id cs

    let setMaxDocs id maxDocs cs = updateWith (Interaction.setMaxDocs maxDocs) id cs

    let setIndexes id idxs cs = updateWith (Interaction.setIndexes idxs) id cs

    let setMode id mode cs = updateWith (Interaction.setMode mode) id cs

    let setFeedback id feedback cs = updateWith (Interaction.setFeedback feedback) id cs

    let feedback id cs = cs |> List.find(fun c -> c.Id = id) |> fun c -> c.Feedback
