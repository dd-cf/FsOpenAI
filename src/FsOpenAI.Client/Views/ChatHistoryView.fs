﻿namespace FsOpenAI.Client.Views
open System
open Bolero
open Bolero.Html
open MudBlazor
open Radzen
open Radzen.Blazor
open FsOpenAI.Client
open Microsoft.AspNetCore.Components
open Microsoft.JSInterop
open FsOpenAI.Shared
open FsOpenAI.Shared.Interactions

module MessageViews = 
    let userMessage (m:InteractionMessage) (chat:Interaction) model dispatch = 
        let bg = "background-color: transparent;"
        comp<RadzenRow> {                                        
            "Style" => bg
            attr.``class`` "rz-mt-1 rz-mr-2"
            comp<RadzenColumn> {
                "Size" => 12
                attr.style "display: flex; justify-content: flex-end;"
                comp<RadzenStack> {
                    "Orientation" => Orientation.Horizontal
                    attr.``class`` $"rz-border-radius-5 rz-p-8; rz-background-color-info-lighter"
                    div {
                        attr.style "white-space: pre-line;"
                        attr.``class`` "rz-p-2"
                        text m.Message
                    }
                    comp<RadzenMenu> {
                        "Style" => bg
                        "Responsive" => false
                        comp<RadzenMenuItem> {
                            "Icon" => "refresh"
                            attr.title "Edit and resubmit this message"
                            attr.callback "Click" (fun (e:MenuItemEventArgs) -> dispatch (Ia_Restart (chat.Id, m)))
                        }
                    }                
                }
            }
        }
    
    let systemMessage (msg:InteractionMessage) (chat:Interaction) lastMsg model dispatch =
        let docs = match msg.Role with Assistant r -> r.Docs | _ -> []
        comp<RadzenCard> {
            attr.``class`` $"rz-mt-1 rz-border-radius-3"
            comp<RadzenRow> {
                comp<RadzenColumn> {
                    "Size" => 1
                    comp<RadzenIcon> {
                        "Icon" => "robot_2" // C.DFLT_ASST_ICON
                        "IconStyle" => IconStyle.Info
                    }
                }
                comp<RadzenColumn> {
                    "Size" => 10
                    div {
                        attr.style "white-space: pre-line;"
                        if Utils.isEmpty msg.Message then "..." else msg.Message
                    }
                    table {
                        attr.``class`` "rz-background-color-info-lighter"
                        if not docs.IsEmpty && not chat.IsBuffering then
                            tr {
                                attr.``class`` "rz-mt-1"
                                td {
                                    attr.style "width: 1.5rem;"
                                    comp<RadzenMenu> {
                                        "Style" => "background-color: transparent;"
                                        "Responsive" => false
                                        comp<RadzenMenuItem> {
                                            "Icon" => "snippet_folder"
                                            attr.title "Show search results"
                                            attr.callback "Click" (fun (e:MenuItemEventArgs) -> dispatch (Ia_ToggleDocs (chat.Id, Some msg.MsgId)))
                                        }
                                    }                                                                   
                                }
                                td {
                                    comp<RadzenStack> {
                                        attr.``class`` "rz-p-2"
                                        "Style" => "height: 3.5rem; overflow: auto;"
                                        "Orientation" => Orientation.Horizontal
                                        "Wrap" => Wrap.Wrap
                                        for d in docs do
                                            comp<RadzenLink> {
                                                attr.title (Utils.shorten 40 d.Text)
                                                attr.``class`` "rz-ml-2"                                                
                                                "Style" => "max-width: 140px; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical;"
                                                "Path" => d.Ref
                                                "Target" => "_blank"
                                                d.Title
                                            }
                                    }
                                }
                            }
                    }
                }
                comp<RadzenColumn> {
                    "Size" => 1
                    "Style" => "display:flex; flex-direction: column; justify-content: space-between;"
                    comp<RadzenMenu> {
                        "Responsive" => false
                        comp<RadzenMenuItem> {
                            "Disabled" => true
                            "Icon" => ""
                            attr.callback "Click" (fun (e:MenuItemEventArgs) -> dispatch ToggleSideBar)
                        }
                    }
                    if lastMsg && chat.Feedback.IsSome && not chat.IsBuffering then
                        comp<RadzenMenu> {
                            "Responsive" => false
                            comp<RadzenMenuItem> {
                                "Icon" => "thumbs_up_down"
                                attr.title "Feedback"
                                attr.callback "Click" (fun (e:MenuItemEventArgs) -> dispatch ToggleSideBar)
                            }
                        }
                }
            }
        }

type ChatHistoryView() =
    inherit ElmishComponent<Model,Message>()

    let marker = HtmlRef()

    member val IsBuffering = false with get, set
    member val markerId = "" with get, set

    [<Inject>]
    member val JSRuntime = Unchecked.defaultof<IJSRuntime> with get, set

    member this.CopyToClipboard(text:string) =
        this.JSRuntime.InvokeVoidAsync ("navigator.clipboard.writeText", text) |> ignore

    member this.ScrollToEnd() =
        if Utils.notEmpty this.markerId then
            this.JSRuntime.InvokeVoidAsync ("fso_scrollTo", [|this.markerId|]) |> ignore

    override this.OnAfterRenderAsync(a) =
        if this.IsBuffering then this.ScrollToEnd()
        base.OnAfterRenderAsync(a)

    override this.View model dispatch =
        comp<RadzenRow> {
            attr.``class`` "rz-mr-1"
            comp<RadzenColumn> { 
                comp<RadzenRow> {
                    comp<RadzenColumn> {
                        "Sytle" => "height: 1rem;"
                        //"Size" => 1
                        match Model.selectedChat model with
                        | Some chat ->
                            let m = 
                                {
                                    ChatId = chat.Id
                                    Model = model
                                    Parms = chat.Parameters
                                    QaBag = Interaction.qaBag chat
                                    SystemMessage = Interaction.systemMessage chat
                                }
                            ecomp<ChatSettingsView,_,_> m dispatch {attr.empty()}
                        | None ->   ()
                    }
                }
                comp<RadzenRow> {
                    "Style" => "max-height: calc(100vh - 19rem);overflow-y:auto;overflow-x:hidden;"
                    comp<RadzenColumn> {
                        match Model.selectedChat model with
                        | Some chat ->                             
                            let lastMsgId = List.tryLast chat.Messages |> Option.map (fun x -> x.MsgId) |> Option.defaultValue ""
                            this.markerId <- $"{chat.Id}_marker"
                            this.IsBuffering <- chat.IsBuffering
                            for m in chat.Messages do
                                if m.IsUser then
                                    yield MessageViews.userMessage m chat model dispatch
                                else

                                    yield MessageViews.systemMessage m chat  (m.MsgId=lastMsgId) model dispatch
                            if chat.IsBuffering then
                                yield 
                                    div {
                                        attr.id this.markerId
                                        concat {
                                            yield ul {attr.empty() }
                                            for t in chat.Notifications do
                                                yield 
                                                    li {                                                        
                                                        comp<RadzenLabel> {
                                                            attr.``class`` "rz-color-info-light"
                                                            "Text" => t
                                                        }
                                                    }
                                        }
                                        div {
                                            attr.id this.markerId
                                            marker
                                            "..."
                                        }
                                    }
                        | None -> ()
                    }
                }
                comp<RadzenRow> {
                    "Style" => "height: auto; margin-right: 1rem; margin-top: 1rem;" 
                    ecomp<QuestionView,_,_> model dispatch {attr.empty()}
                }
            }
        }
