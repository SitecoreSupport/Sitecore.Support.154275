// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Workbox.form.cs" company="Sitecore">
//   Copyright (c) Sitecore. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Sitecore.Support.Shell.Applications.Workbox
{
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.Linq;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Web;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Exceptions;
  using Sitecore.Globalization;
  using Sitecore.Pipelines;
  using Sitecore.Pipelines.GetWorkflowCommentsDisplay;
  using Sitecore.Resources;
  using Sitecore.Shell.Data;
  using Sitecore.Shell.Feeds;
  using Sitecore.Shell.Framework;
  using Sitecore.Shell.Framework.CommandBuilders;
  using Sitecore.Shell.Framework.Commands;
  using Sitecore.Text;
  using Sitecore.Web;
  using Sitecore.Web.UI.HtmlControls;
  using Sitecore.Web.UI.Sheer;
  using Sitecore.Web.UI.WebControls.Ribbons;
  using Sitecore.Web.UI.XmlControls;
  using Sitecore.Workflows;
  using Sitecore.Workflows.Simple;

  /// <summary>
  /// Displays the workbox.
  /// </summary>
  public class WorkboxForm : BaseForm
  {
    #region Constants and Fields

    /// <summary>
    /// The pager.
    /// </summary>
    protected Border Pager;

    /// <summary>
    /// The ribbon panel.
    /// </summary>
    protected Border RibbonPanel;

    /// <summary>
    /// The states.
    /// </summary>
    protected Border States;

    /// <summary>
    /// The view menu.
    /// </summary>
    protected Toolmenubutton ViewMenu;

    /// <summary>
    /// The _state names.
    /// </summary>
    private NameValueCollection stateNames;

    /// <summary>
    /// The maximum length allowed for a comment text.
    /// </summary>
    private readonly int CommentMaxLength = 2000;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the size of the page.
    /// </summary>
    /// <value>The size of the page.</value>
    public int PageSize
    {
      get
      {
        return Registry.GetInt("/Current_User/Workbox/Page Size", 10);
      }

      set
      {
        Registry.SetInt("/Current_User/Workbox/Page Size", value);
      }
    }

    /// <summary>
    /// Gets or sets the offset(what page we are on).
    /// </summary>
    /// <value>The size of the offset.</value>
    private OffsetCollection Offset = new OffsetCollection();
    private class OffsetCollection
    {
      public int this[string key]
      {
        get
        {
          if (Context.ClientPage.ServerProperties[key] != null)
          {
            var myVal = (int)Context.ClientPage.ServerProperties[key];
            return myVal;
          }

          //Key not in viewstate, could have been redirect so the viewstate is empty
          var url = new UrlString(WebUtil.GetRawUrl());
          if (url[key] != null)
          {
            int offSet;
            return int.TryParse(url[key], out offSet) ? offSet : 0;
          }

          return 0;
        }
        set
        {
          Context.ClientPage.ServerProperties[key] = value;
        }
      }
    }

    /// <summary>
    /// Gets a value indicating whether page is reloads by reload button on the ribbon.
    /// </summary>
    /// <value><c>true</c> if this instance is reload; otherwise, <c>false</c>.</value>
    protected virtual bool IsReload
    {
      get
      {
        var url = new UrlString(WebUtil.GetRawUrl());
        return url["reload"] == "1";
      }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Comments the specified args.
    /// </summary>
    /// <param name="args">
    /// The arguments.
    /// </param>
    public void Comment([NotNull] ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      var items = Context.ClientPage.ServerProperties["items"];

      Assert.IsNotNull(items, "Items is null");

      List<ItemUri> itemUris = (List<ItemUri>)items;

      var commandId = ID.Null;
      if (Context.ClientPage.ServerProperties["command"] != null)
        ID.TryParse(Context.ClientPage.ServerProperties["command"] as string, out commandId);

      // todo: Skynet: get rid of magic strings
      var bypassUI = (args.Parameters["ui"] != null && args.Parameters["ui"] == "1") ||
        (args.Parameters["suppresscomment"] != null && args.Parameters["suppresscomment"] == "1");

      if (!args.IsPostBack && commandId != (ID)null && !bypassUI)
      {
        DisplayCommentDialog(itemUris, commandId, args);
      }
      else if ((args.Result != null && args.Result != "null" && args.Result != "undefined" && args.Result != "cancel") || bypassUI)
      {
        var comment = args.Result;
        Collections.StringDictionary fields = new Collections.StringDictionary();
        string workflowStateId = string.Empty;

        if (Context.ClientPage.ServerProperties["workflowStateid"] != null)
        {
          workflowStateId = Context.ClientPage.ServerProperties["workflowStateid"].ToString();
        }

        string command = Context.ClientPage.ServerProperties["command"].ToString();

        var workflow = this.GetWorkflowFromPage();
        if (workflow == null)
        {
          return;
        }

        if (!string.IsNullOrEmpty(comment))
        {
          fields = WorkflowUIHelper.ExtractFieldsFromFieldEditor(comment);
        }

        if (!string.IsNullOrWhiteSpace(fields["Comments"]) && fields["Comments"].Length > CommentMaxLength)
        {
          Context.ClientPage.ClientResponse.Alert(
            string.Format(Texts.TheCommentIsTooLongYouHaveEntered0CharactersACommentCanno, fields["Comments"].Length));

          DisplayCommentDialog(itemUris, commandId, args);

          return;
        }

        ExecutCommand(itemUris, workflow, fields, command, workflowStateId);

        Refresh();
      }
    }

    /// <summary>
    /// Handles the message.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    public override void HandleMessage([NotNull] Message message)
    {
      Assert.ArgumentNotNull(message, "message");

      switch (message.Name)
      {
        case "workflow:send":
          this.Send(message);
          return;

        case "workflow:sendselected":
          this.SendSelected(message);
          return;

        case "workflow:sendall":
          this.SendAll(message);
          return;

        case "window:close":
          Windows.Close();
          return;

        case "workflow:showhistory":
          ShowHistory(message, Context.ClientPage.ClientRequest.Control);
          return;

        case "workbox:hide":
          Context.ClientPage.SendMessage(this, "pane:hide(id=" + message["id"] + ")");
          Context.ClientPage.ClientResponse.SetAttribute("Check_Check_" + message["id"], "checked", "false");
          break;

        case "pane:hidden":
          Context.ClientPage.ClientResponse.SetAttribute("Check_Check_" + message["paneid"], "checked", "false");
          break;

        case "workbox:show":
          Context.ClientPage.SendMessage(this, "pane:show(id=" + message["id"] + ")");
          Context.ClientPage.ClientResponse.SetAttribute("Check_Check_" + message["id"], "checked", "true");
          break;

        case "pane:showed":
          Context.ClientPage.ClientResponse.SetAttribute("Check_Check_" + message["paneid"], "checked", "true");
          break;
      }

      base.HandleMessage(message);

      string id = message["id"];

      if (!string.IsNullOrEmpty(id))
      {
        string language = StringUtil.GetString(message["language"]);
        string version = StringUtil.GetString(message["version"]);

        Item item = Context.ContentDatabase.Items[id, Language.Parse(language), Sitecore.Data.Version.Parse(version)];

        if (item != null)
        {
          Dispatcher.Dispatch(message, item);
        }
      }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Diffs the specified id.
    /// </summary>
    /// <param name="id">
    /// The id.
    /// </param>
    /// <param name="language">
    /// The language.
    /// </param>
    /// <param name="version">
    /// The version.
    /// </param>
    protected void Diff([NotNull] string id, [NotNull] string language, [NotNull] string version)
    {
      Assert.ArgumentNotNull(id, "id");
      Assert.ArgumentNotNull(language, "language");
      Assert.ArgumentNotNull(version, "version");

      var url = new UrlString(UIUtil.GetUri("control:Diff"));

      url.Append("id", id);
      url.Append("la", language);
      url.Append("vs", version);
      url.Append("wb", "1");

      Context.ClientPage.ClientResponse.ShowModalDialog(url.ToString());
    }

    /// <summary>
    /// Displays the state.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="state">
    /// The state.
    /// </param>
    /// <param name="stateItems">
    /// The item for the workflow state.
    /// </param>
    /// <param name="control">
    /// The control.
    /// </param>
    /// <param name="offset">
    /// The offset.
    /// </param>
    /// <param name="pageSize">
    /// Size of the page.
    /// </param>    
    protected virtual void DisplayState([NotNull] IWorkflow workflow, [NotNull] WorkflowState state, [NotNull] StateItems stateItems, [NotNull] System.Web.UI.Control control, int offset, int pageSize)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(state, "state");
      Assert.ArgumentNotNull(stateItems, "stateItems");
      Assert.ArgumentNotNull(control, "control");

      var items = stateItems.Items.ToArray();

      if (items.Length > 0)
      {
        int end = offset + pageSize;
        if (end > items.Length)
        {
          end = items.Length;
        }

        for (int n = offset; n < end; n++)
        {
          this.CreateItem(workflow, items[n], control);
        }

        var toolbar = new Border
        {
          Background = "#fff"
        };
        control.Controls.Add(toolbar);

        toolbar.Margin = "0 5px 10px 15px";
        toolbar.Padding = "5px 10px";
        toolbar.Class = "scWorkboxToolbarButtons";

        // No need to filter here, the allowed commands should have already been filtered by the caller
        var commands = workflow.GetCommands(state.StateID);
        foreach (var command in commands)
        {
          if (!stateItems.CommandIds.Contains(command.CommandID))
          {
            continue;
          }

          var workboxCommand = Resource.GetWebControl("WorkboxCommand") as XmlControl;
          Assert.IsNotNull(workboxCommand, "workboxCommand is null");

          workboxCommand["Header"] = command.DisplayName + " " + Translate.Text(Texts.SELECTED1);
          workboxCommand["Icon"] = command.Icon;
          workboxCommand["Command"] = "workflow:sendselected(command=" + command.CommandID + ",ws=" + state.StateID +
            ",wf=" + workflow.WorkflowID + ")";

          toolbar.Controls.Add(workboxCommand);

          workboxCommand = Resource.GetWebControl("WorkboxCommand") as XmlControl;
          Assert.IsNotNull(workboxCommand, "workboxCommand is null");

          workboxCommand["Header"] = command.DisplayName + " " + Translate.Text(Texts.ALL1);
          workboxCommand["Icon"] = command.Icon;
          workboxCommand["Command"] = "workflow:sendall(command=" + command.CommandID + ",ws=" + state.StateID + ",wf=" +
            workflow.WorkflowID + ")";

          toolbar.Controls.Add(workboxCommand);
        }
      }
    }

    /// <summary>
    /// Displays the states.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="placeholder">
    /// The placeholder.
    /// </param>
    protected virtual void DisplayStates([NotNull] IWorkflow workflow, [NotNull] XmlControl placeholder)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(placeholder, "placeholder");

      this.stateNames = null;

      foreach (WorkflowState state in workflow.GetStates())
      {
        var stateItems = this.GetStateItems(state, workflow);
        Assert.IsNotNull(stateItems, "stateItems is null");

        // Only render state if there are some commands.
        if (!stateItems.CommandIds.Any())
        {
          continue;
        }

        string id = ShortID.Encode(workflow.WorkflowID) + "_" + ShortID.Encode(state.StateID);

        var section = new Section
        {
          ID = id + "_section"
        };

        placeholder.AddControl(section);

        int count = stateItems.Items.Count();
        string countText;

        if (count <= 0)
        {
          countText = Translate.Text(Texts.NONE);
        }
        else if (count == 1)
        {
          countText = string.Format("1 {0}", Translate.Text(Texts.ITEM1));
        }
        else
        {
          countText = string.Format("{0} {1}", count, Translate.Text(Texts.ITEMS));
        }

        countText = string.Format("<span style=\"font-weight:normal\"> - ({0})</span>", countText);

        section.Header = state.DisplayName + countText;
        section.Icon = state.Icon;

        if (Settings.ClientFeeds.Enabled)
        {
          var feedLink = new FeedUrlOptions("/sitecore/shell/~/feed/workflowstate.aspx")
          {
            UseUrlAuthentication = true
          };
          feedLink.Parameters["wf"] = workflow.WorkflowID;
          feedLink.Parameters["st"] = state.StateID;

          section.FeedLink = feedLink.ToString();
        }

        section.Collapsed = count <= 0;

        var content = new Border();
        section.Controls.Add(content);

        content.ID = id + "_content";

        this.DisplayState(workflow, state, stateItems, content, this.Offset[state.StateID], this.PageSize);

        this.CreateNavigator(section, id + "_navigator", count, this.Offset[state.StateID]);
      }
    }

    /// <summary>
    /// Displays the workflow.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    protected virtual void DisplayWorkflow([NotNull] IWorkflow workflow)
    {
      Assert.ArgumentNotNull(workflow, "workflow");

      Context.ClientPage.ServerProperties["WorkflowID"] = workflow.WorkflowID;

      var pane = Resource.GetWebControl("Pane") as XmlControl;
      Error.AssertXmlControl(pane, "Pane");

      this.States.Controls.Add(pane);

      Assert.IsNotNull(pane, "pane");

      pane["PaneID"] = this.GetPaneID(workflow);
      pane["Header"] = workflow.Appearance.DisplayName;
      pane["Icon"] = workflow.Appearance.Icon;

      var feedLink = new FeedUrlOptions("/sitecore/shell/~/feed/workflow.aspx")
      {
        UseUrlAuthentication = true
      };
      feedLink.Parameters["wf"] = workflow.WorkflowID;

      pane["FeedLink"] = feedLink.ToString();

      this.DisplayStates(workflow, pane);

      if (Context.ClientPage.IsEvent)
      {
        SheerResponse.Insert(this.States.ClientID, "append", HtmlUtil.RenderControl(pane));
      }
    }

    /// <summary>
    /// Raises the load event.
    /// </summary>
    /// <param name="e">
    /// The <see cref="System.EventArgs"/> instance containing the event data.
    /// </param>
    /// <remarks>
    /// This method notifies the server control that it should perform actions common to each HTTP
    /// request for the page it is associated with, such as setting up a database query. At this
    /// stage in the page lifecycle, server controls in the hierarchy are created and initialized,
    /// view state is restored, and form controls reflect client-side data. Use the IsPostBack
    /// property to determine whether the page is being loaded in response to a client postback,
    /// or if it is being loaded and accessed for the first time.
    /// </remarks>
    protected override void OnLoad([NotNull] EventArgs e)
    {
      Assert.ArgumentNotNull(e, "e");

      base.OnLoad(e);

      if (!Context.ClientPage.IsEvent)
      {
        IWorkflowProvider provider = Context.ContentDatabase.WorkflowProvider;

        if (provider != null)
        {
          IWorkflow[] workflows = provider.GetWorkflows();

          foreach (IWorkflow workflow in workflows)
          {
            string id = "P" + Regex.Replace(workflow.WorkflowID, "\\W", String.Empty);

            if (!this.IsReload)
            {
              if (workflows.Length == 1 && string.IsNullOrEmpty(Registry.GetString("/Current_User/Panes/" + id)))
              {
                Registry.SetString("/Current_User/Panes/" + id, "visible");
              }
            }

            if ((Registry.GetString("/Current_User/Panes/" + id) ?? string.Empty) == "visible")
            {
              this.DisplayWorkflow(workflow);
            }
          }
        }

        this.UpdateRibbon();
      }

      this.WireUpNavigators(Context.ClientPage);
    }

    /// <summary>
    /// Called when the view menu is clicked.
    /// </summary>
    protected void OnViewMenuClick()
    {
      Sitecore.Web.UI.HtmlControls.Menu menu = new Sitecore.Web.UI.HtmlControls.Menu();

      IWorkflowProvider provider = Context.ContentDatabase.WorkflowProvider;

      if (provider != null)
      {
        foreach (IWorkflow workflow in provider.GetWorkflows())
        {
          string paneID = this.GetPaneID(workflow);

          string state = Registry.GetString("/Current_User/Panes/" + paneID);

          string message = state != "hidden" ? "workbox:hide" : "workbox:show";

          menu.Add(
            Control.GetUniqueID("ctl"),
            workflow.Appearance.DisplayName,
            workflow.Appearance.Icon,
            String.Empty,
            message + "(id=" + paneID + ")",
            state != "hidden",
            String.Empty,
            MenuItemType.Check);
        }

        if (menu.Controls.Count > 0)
        {
          menu.AddDivider();
        }

        menu.Add("Refresh", "Office/16x16/refresh.png", "Refresh");
      }

      Context.ClientPage.ClientResponse.ShowPopup("ViewMenu", "below", menu);
    }

    /// <summary>
    /// Opens the specified item.
    /// </summary>
    /// <param name="id">
    /// The id.
    /// </param>
    /// <param name="language">
    /// The language.
    /// </param>
    /// <param name="version">
    /// The version.
    /// </param>
    protected void Open([NotNull] string id, [NotNull] string language, [NotNull] string version)
    {
      Assert.ArgumentNotNull(id, "id");
      Assert.ArgumentNotNull(language, "language");
      Assert.ArgumentNotNull(version, "version");

      string root = RootSections.GetSectionID(id);

      var url = new UrlString();

      url.Append("ro", root);
      url.Append("fo", id);
      url.Append("id", id);
      url.Append("la", language);
      url.Append("vs", version);

      Windows.RunApplication("Content editor", url.ToString());
    }

    /// <summary>
    /// Called with the pages size changes.
    /// </summary>
    protected void PageSize_Change()
    {
      string value = Context.ClientPage.ClientRequest.Form["PageSize"];

      int size = MainUtil.GetInt(value, 10);

      this.PageSize = size;

      this.Refresh();
    }

    /// <summary>
    /// Toggles the pane.
    /// </summary>
    /// <param name="id">
    /// The id.
    /// </param>
    protected void Pane_Toggle([NotNull] string id)
    {
      Assert.ArgumentNotNull(id, "id");

      string workboxId = "P" + Regex.Replace(id, "\\W", String.Empty);

      string state = Registry.GetString("/Current_User/Panes/" + workboxId);

      if (Context.ClientPage.FindControl(workboxId) == null)
      {
        IWorkflowProvider provider = Context.ContentDatabase.WorkflowProvider;

        if (provider == null)
        {
          return;
        }

        IWorkflow workflow = provider.GetWorkflow(id);
        this.DisplayWorkflow(workflow);
      }

      if (string.IsNullOrEmpty(state) || state == "hidden")
      {
        Registry.SetString("/Current_User/Panes/" + workboxId, "visible");
        Context.ClientPage.ClientResponse.SetStyle(workboxId, "display", string.Empty);
      }
      else
      {
        Registry.SetString("/Current_User/Panes/" + workboxId, "hidden");
        Context.ClientPage.ClientResponse.SetStyle(workboxId, "display", "none");
      }

      SheerResponse.SetReturnValue(true);
    }

    /// <summary>
    /// Previews the specified item.
    /// </summary>
    /// <param name="id">
    /// The id.
    /// </param>
    /// <param name="language">
    /// The language.
    /// </param>
    /// <param name="version">
    /// The version.
    /// </param>
    protected void Preview([NotNull] string id, [NotNull] string language, [NotNull] string version)
    {
      Assert.ArgumentNotNull(id, "id");
      Assert.ArgumentNotNull(language, "language");
      Assert.ArgumentNotNull(version, "version");

      Context.ClientPage.SendMessage(
        this, "item:preview(id=" + id + ",language=" + language + ",version=" + version + ")");
    }

    /// <summary>
    /// Refreshes the page.
    /// </summary>
    protected virtual void Refresh()
    {
      this.Refresh(null);
    }


    /// <summary>
    /// Refreshes the page.
    /// </summary>
    /// <param name="urlArguments">The URL arguments.</param>
    protected void Refresh(Dictionary<string, string> urlArguments)
    {
      var url = new UrlString(WebUtil.GetRawUrl());
      url["reload"] = "1";

      if (urlArguments != null)
      {
        foreach (var urlArgument in urlArguments)
        {
          url[urlArgument.Key] = urlArgument.Value;
        }
      }

      // Relative URL was causing 'malformed URI' exceptions in the browser when set from certain postbacks (workflow command with UI action)
      var fullUrl = WebUtil.GetFullUrl(url.ToString());

      Context.ClientPage.ClientResponse.SetLocation(fullUrl);
    }

    /// <summary>
    /// Shows the history.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    /// <param name="control">
    /// The control.
    /// </param>
    private static void ShowHistory([NotNull] Message message, [NotNull] string control)
    {
      Assert.ArgumentNotNull(message, "message");
      Assert.ArgumentNotNull(control, "control");

      var history = Resource.GetWebControl("WorkboxHistory") as XmlControl;
      Assert.IsNotNull(history, "history is null");

      history["ItemID"] = message["id"];
      history["Language"] = message["la"];
      history["Version"] = message["vs"];
      history["WorkflowID"] = message["wf"];

      Context.ClientPage.ClientResponse.ShowPopup(control, "below", history);
    }

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="command">
    /// The command.
    /// </param>
    /// <param name="item">
    /// The item.
    /// </param>
    /// <param name="workboxItem">
    /// The workbox item.
    /// </param>
    private void CreateCommand([NotNull] IWorkflow workflow, [NotNull] WorkflowCommand command, [NotNull] Item item, [NotNull] XmlControl workboxItem)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(command, "command");
      Assert.ArgumentNotNull(item, "item");
      Assert.ArgumentNotNull(workboxItem, "workboxItem");

      var workboxCommand = Resource.GetWebControl("WorkboxCommand") as XmlControl;
      Assert.IsNotNull(workboxCommand, "workboxCommand is null");

      workboxCommand["Header"] = command.DisplayName;
      workboxCommand["Icon"] = command.Icon;

      var builder = new CommandBuilder("workflow:send");
      builder.Add("id", item.ID.ToString());
      builder.Add("la", item.Language.Name);
      builder.Add("vs", item.Version.ToString());
      builder.Add("command", command.CommandID);
      builder.Add("wf", workflow.WorkflowID);
      builder.Add("ui", command.HasUI);
      builder.Add("suppresscomment", command.SuppressComment);

      workboxCommand["Command"] = builder.ToString();

      workboxItem.AddControl(workboxCommand);
    }

    /// <summary>
    /// Creates the item.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="item">
    /// The item.
    /// </param>
    /// <param name="control">
    /// The control.
    /// </param>
    private void CreateItem([NotNull] IWorkflow workflow, [NotNull] Item item, [NotNull] System.Web.UI.Control control)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(item, "item");
      Assert.ArgumentNotNull(control, "control");

      var workboxItem = Resource.GetWebControl("WorkboxItem") as XmlControl;
      Assert.IsNotNull(workboxItem, "workboxItem is null");

      control.Controls.Add(workboxItem);

      var details = new StringBuilder(" - (");
      Language language = item.Language;
      details.Append(language.CultureInfo.DisplayName);
      details.Append(", ");
      details.Append(Translate.Text(Texts.VERSION1));
      details.Append(' ');
      details.Append(item.Version.ToString());
      details.Append(")");

      Assert.IsNotNull(workboxItem, "workboxItem");

      WorkflowEvent[] events = workflow.GetHistory(item);

      workboxItem["Header"] = item.GetUIDisplayName();
      workboxItem["Details"] = details.ToString();
      workboxItem["Icon"] = item.Appearance.Icon;
      workboxItem["ShortDescription"] = Settings.ContentEditor.RenderItemHelpAsHtml ? WebUtil.RemoveAllScripts(item.Help.ToolTip) : HttpUtility.HtmlEncode(item.Help.ToolTip);
      workboxItem["History"] = GetHistory(workflow, events);
      workboxItem["LastComments"] = HttpUtility.HtmlEncode(GetLastComments(events, item));
      workboxItem["HistoryMoreID"] = Control.GetUniqueID("ctl");
      workboxItem["HistoryClick"] = "workflow:showhistory(id=" + item.ID + ",la=" + item.Language.Name + ",vs=" +
        item.Version + ",wf=" + workflow.WorkflowID + ")";
      workboxItem["PreviewClick"] = "Preview(\"" + item.ID + "\", \"" + item.Language + "\", \"" + item.Version + "\")";
      workboxItem["Click"] = "Open(\"" + item.ID + "\", \"" + item.Language + "\", \"" + item.Version + "\")";
      workboxItem["DiffClick"] = "Diff(\"" + item.ID + "\", \"" + item.Language + "\", \"" + item.Version + "\")";
      workboxItem["Display"] = "none";

      string id = Control.GetUniqueID(String.Empty);

      workboxItem["CheckID"] = "check_" + id;
      workboxItem["HiddenID"] = "hidden_" + id;
      workboxItem["CheckValue"] = item.ID + "," + item.Language + "," + item.Version;

      foreach (WorkflowCommand command in WorkflowFilterer.FilterVisibleCommands(workflow.GetCommands(item), item))
      {
        this.CreateCommand(workflow, command, item, workboxItem);
      }
    }

    /// <summary>
    /// Creates the navigator.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="id">The id.</param>
    /// <param name="count">The count.</param>
    /// <param name="offset">The offset.</param>
    private void CreateNavigator([NotNull] Section section, [NotNull] string id, int count, int offset)
    {
      Assert.ArgumentNotNull(section, "section");
      Assert.ArgumentNotNull(id, "id");

      var navigator = new Navigator();

      section.Controls.Add(navigator);

      navigator.ID = id;
      navigator.Offset = offset;
      navigator.Count = count;
      navigator.PageSize = this.PageSize;
    }

    /// <summary>
    /// Gets the history.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="events">
    /// The workflow history for the item
    /// </param>
    /// <returns>
    /// The get history.
    /// </returns>
    [NotNull]
    private string GetHistory([NotNull] IWorkflow workflow, [NotNull] WorkflowEvent[] events)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(events, "events");

      string result;

      if (events.Length > 0)
      {
        WorkflowEvent evt = events[events.Length - 1];

        string user = evt.User;
        string domainName = Context.Domain.Name;

        if (user.StartsWith(domainName + "\\", StringComparison.OrdinalIgnoreCase))
        {
          user = StringUtil.Mid(user, domainName.Length + 1);
        }

        user = StringUtil.GetString(user, Translate.Text(Texts.UNKNOWN2));

        string from = this.GetStateName(workflow, evt.OldState);
        string to = this.GetStateName(workflow, evt.NewState);

        result = String.Format(
          Translate.Text(Texts._0_CHANGED_FROM_1_TO_2_ON_3), user, from, to, DateUtil.FormatDateTime(DateUtil.ToServerTime(evt.Date), "D", Context.User.Profile.Culture));
      }
      else
      {
        result = Translate.Text(Texts.NO_CHANGES_HAS_BEEN_MADE);
      }

      return result;
    }

    /// <summary>
    /// Get the comments from the latest workflow event
    /// </summary>
    /// <param name="events">The workflow events to process</param>
    /// <param name="item">The item to get the comment for</param>
    /// <returns>The last comments</returns>
    private string GetLastComments([NotNull] WorkflowEvent[] events, [NotNull] Item item)
    {
      Assert.ArgumentNotNull(events, "events");

      if (events.Length > 0)
      {
        WorkflowEvent evt = events[events.Length - 1];

        return GetWorkflowCommentsDisplayPipeline.Run(evt, item);
      }

      return string.Empty;
    }

    /// <summary>
    /// Gets the items.
    /// </summary>
    /// <param name="state">
    /// The state.
    /// </param>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <returns>
    /// Array of item DataUri.
    /// </returns>
    [CanBeNull]
    protected virtual DataUri[] GetItems([NotNull] WorkflowState state, [NotNull] IWorkflow workflow)
    {
      Assert.ArgumentNotNull(state, "state");
      Assert.ArgumentNotNull(workflow, "workflow");

      Assert.Required(Context.ContentDatabase, "Context.ContentDatabase");

      DataUri[] items = workflow.GetItems(state.StateID);

      if ((items == null) || items.Length == 0)
        return new DataUri[0];

      var result = new ArrayList(items.Length);

      foreach (DataUri uri in items)
      {
        Item item = Context.ContentDatabase.GetItem(uri);
        //Cound not fetch item due to security, or removed from db.        
        if (item == null)
          continue;

        if (item.Access.CanRead() && item.Access.CanReadLanguage() && item.Access.CanWriteLanguage() &&
          (Context.IsAdministrator || item.Locking.CanLock() || item.Locking.HasLock()))
        {
          result.Add(uri);
        }
      }

      return result.ToArray(typeof(DataUri)) as DataUri[];
    }

    /// <summary>
    /// Gets the items in the workflow state.
    /// </summary>
    /// <param name="state">The state to get the items for.</param>
    /// <param name="workflow">The workflow the state belongs to.</param>
    /// <returns>The items for the state.</returns>
    private StateItems GetStateItems([NotNull] WorkflowState state, [NotNull] IWorkflow workflow)
    {
      Assert.ArgumentNotNull(state, "state");
      Assert.ArgumentNotNull(workflow, "workflow");

      var result = new List<Item>();
      var commands = new List<string>();

      var items = workflow.GetItems(state.StateID);
      var tooManyItems = items.Length > Settings.Workbox.StateCommandFilteringItemThreshold;

      if (items != null)
      {
        foreach (var uri in items)
        {
          Item item = Context.ContentDatabase.GetItem(uri);

          if (item != null)
          {
            if (item.Access.CanRead() && item.Access.CanReadLanguage() && item.Access.CanWriteLanguage() &&
              (Context.IsAdministrator || item.Locking.CanLock() || item.Locking.HasLock()))
            {
              result.Add(item);

              if (!tooManyItems)
              {
                var itemCommands = WorkflowFilterer.FilterVisibleCommands(workflow.GetCommands(item), item);

                foreach (var itemCommand in itemCommands)
                {
                  if (!commands.Contains(itemCommand.CommandID))
                    commands.Add(itemCommand.CommandID);
                }
              }
            }
          }
        }
      }

      if (tooManyItems)
      {
        var stateCommands = WorkflowFilterer.FilterVisibleCommands(workflow.GetCommands(state.StateID));
        commands.AddRange(stateCommands.Select(x => x.CommandID));
      }

      return new StateItems
      {
        Items = result,
        CommandIds = commands
      };
    }

    /// <summary>
    /// Gets the pane ID.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <returns>
    /// The get pane id.
    /// </returns>
    [NotNull]
    private string GetPaneID([NotNull] IWorkflow workflow)
    {
      Assert.ArgumentNotNull(workflow, "workflow");

      return "P" + Regex.Replace(workflow.WorkflowID, "\\W", String.Empty);
    }

    /// <summary>
    /// Gets the name of the state.
    /// </summary>
    /// <param name="workflow">
    /// The workflow.
    /// </param>
    /// <param name="stateID">
    /// The state ID.
    /// </param>
    /// <returns>
    /// The get state name.
    /// </returns>
    [NotNull]
    private string GetStateName([NotNull] IWorkflow workflow, [NotNull] string stateID)
    {
      Assert.ArgumentNotNull(workflow, "workflow");
      Assert.ArgumentNotNull(stateID, "stateID");

      if (this.stateNames == null)
      {
        this.stateNames = new NameValueCollection();

        foreach (WorkflowState state in workflow.GetStates())
        {
          this.stateNames.Add(state.StateID, state.DisplayName);
        }
      }

      return StringUtil.GetString(this.stateNames[stateID], "?");
    }

    /// <summary>
    /// Jumps the specified sender.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="message">
    /// The message.
    /// </param>
    /// <param name="offset">
    /// The offset.
    /// </param>
    private void Jump([NotNull] object sender, [NotNull] Message message, int offset)
    {
      Assert.ArgumentNotNull(sender, "sender");
      Assert.ArgumentNotNull(message, "message");

      string id = Context.ClientPage.ClientRequest.Control;

      string workflowID = ShortID.Decode(id.Substring(0, ShortID.Length));
      string stateID = ShortID.Decode(id.Substring(ShortID.Length + 1, ShortID.Length));
      id = id.Substring(0, (ShortID.Length * 2) + 1);

      this.Offset[stateID] = offset;

      IWorkflowProvider provider = Context.ContentDatabase.WorkflowProvider;
      Assert.IsNotNull(provider, "Workflow provider for database \"" + Context.ContentDatabase.Name + "\" not found.");

      IWorkflow workflow = provider.GetWorkflow(workflowID);
      Error.Assert(workflow != null, "Workflow \"" + workflowID + "\" not found.");

      Assert.IsNotNull(workflow, "workflow");

      WorkflowState state = workflow.GetState(stateID);
      Assert.IsNotNull(state, "Workflow state \"" + stateID + "\" not found.");

      var content = new Border
      {
        ID = id + "_content"
      };

      var stateItems = this.GetStateItems(state, workflow);

      this.DisplayState(workflow, state, stateItems ?? new StateItems(), content, offset, this.PageSize);

      Context.ClientPage.ClientResponse.SetOuterHtml(id + "_content", content);
    }

    /// <summary>
    /// Sends the specified message.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    private void Send([NotNull] Message message)
    {
      Assert.ArgumentNotNull(message, "message");

      IWorkflowProvider workflowProvider = Context.ContentDatabase.WorkflowProvider;

      if (workflowProvider != null)
      {
        string workflowID = message["wf"];
        IWorkflow workflow = workflowProvider.GetWorkflow(workflowID);

        if (workflow != null)
        {
          Item item =
            Context.ContentDatabase.Items[message["id"], Language.Parse(message["la"]), Sitecore.Data.Version.Parse(message["vs"])];

          if (item != null)
          {

            InitializeCommentDialog(new List<ItemUri>() { item.Uri }, message);
          }
        }
      }
    }

    /// <summary>
    /// Sends all.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    private void SendAll([NotNull] Message message)
    {
      Assert.ArgumentNotNull(message, "message");

      List<ItemUri> itemUris = new List<ItemUri>();

      string workflowID = message["wf"];
      string workflowStateID = message["ws"];

      IWorkflowProvider workflowProvider = Context.ContentDatabase.WorkflowProvider;

      if (workflowProvider == null)
      {
        return;
      }

      IWorkflow workflow = workflowProvider.GetWorkflow(workflowID);

      if (workflow == null)
      {
        return;
      }

      WorkflowState state = workflow.GetState(workflowStateID);
      DataUri[] uris = this.GetItems(state, workflow);

      Assert.IsNotNull(uris, "uris is null");

      if (uris.Length == 0)
      {
        Context.ClientPage.ClientResponse.Alert(Texts.THERE_ARE_NO_SELECTED_ITEMS);

        return;
      }

      itemUris = uris.Select(du => new ItemUri(du.ItemID, du.Language, du.Version, Context.ContentDatabase)).ToList();

      if (Settings.Workbox.WorkBoxSingleCommentForBulkOperation)
      {
        InitializeCommentDialog(itemUris, message);
      }
      else
      {
        ExecutCommand(itemUris, workflow, null, message["command"], message["ws"]);

        Refresh();
      }
    }

    /// <summary>
    /// Workflow callback to refresh the UI.
    /// </summary>
    /// <param name="args">The args for the workflow execution.</param>
    [UsedImplicitly]
    private void WorkflowCompleteRefresh(WorkflowPipelineArgs args)
    {
      this.Refresh();
    }

    /// <summary>
    /// Sends the selected.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    private void SendSelected([NotNull] Message message)
    {
      Assert.ArgumentNotNull(message, "message");

      List<ItemUri> itemUris = new List<ItemUri>();

      IWorkflowProvider workflowProvider = Context.ContentDatabase.WorkflowProvider;

      if (workflowProvider == null)
      {
        return;
      }

      string workflowID = message["wf"];
      IWorkflow workflow = workflowProvider.GetWorkflow(workflowID);

      if (workflow == null)
      {
        return;
      }

      foreach (string key in Context.ClientPage.ClientRequest.Form.Keys)
      {
        if (key != null && key.StartsWith("check_", StringComparison.InvariantCulture))
        {
          string hiddenKey = "hidden_" + key.Substring(6);
          string value = Context.ClientPage.ClientRequest.Form[hiddenKey];

          string[] parts = value.Split(',');

          if (parts.Length != 3)
          {
            continue;
          }

          var itemUri = new ItemUri(parts[0] ?? string.Empty, Language.Parse(parts[1]), Sitecore.Data.Version.Parse(parts[2]), Context.ContentDatabase);

          itemUris.Add(itemUri);
        }
      }

      if (itemUris.Count == 0)
      {
        Context.ClientPage.ClientResponse.Alert(Texts.THERE_ARE_NO_SELECTED_ITEMS);

        return;
      }

      if (Settings.Workbox.WorkBoxSingleCommentForBulkOperation)
      {
        InitializeCommentDialog(itemUris, message);
      }
      else
      {
        ExecutCommand(itemUris, workflow, null, message["command"], message["ws"]);

        Refresh();
      }
    }

    /// <summary>
    /// Displays a single comment dialog box for multiple selected items.
    /// </summary>
    /// <param name="itemUris">A list of ItemUris</param>
    /// <param name="message">A sheer message value</param>
    private void InitializeCommentDialog(List<ItemUri> itemUris, Message message)
    {
      Context.ClientPage.ServerProperties["items"] = itemUris;
      Context.ClientPage.ServerProperties["command"] = message["command"];
      Context.ClientPage.ServerProperties["workflowid"] = message["wf"];
      Context.ClientPage.ServerProperties["workflowStateid"] = message["ws"];

      Context.ClientPage.Start(this, "Comment", new NameValueCollection
      {
        { "ui", message["ui"] },
        { "suppresscomment", message["suppresscomment"] }
      });
    }

    /// <summary>
    /// Displays comment dialog for the selected items
    /// </summary>
    /// <param name="itemUris">A List of ItemUris</param>
    /// <param name="commandId">The Command Id</param>
    /// <param name="args">Pipeline Args</param>
    protected virtual void DisplayCommentDialog(List<ItemUri> itemUris, ID commandId, ClientPipelineArgs args)
    {
      WorkflowUIHelper.DisplayCommentDialog(itemUris, commandId);

      args.WaitForPostBack();
    }
    #endregion
    #region Fix
    /// <summary>
    /// Executes specific command on multiple selected items
    /// </summary>
    /// <param name="itemUris">A list of ItemUris</param>
    /// <param name="workflow">The workflow</param>
    /// <param name="fields">Fileds dictionary</param>
    /// <param name="command">The command</param>
    /// <param name="workflowStateId">The Workflow State ID</param>
    protected virtual void ExecutCommand(List<ItemUri> itemUris, IWorkflow workflow, Collections.StringDictionary fields, string command, string workflowStateId)
    {
      bool isFailed = false;

      if (fields == null)
      {
        fields = new Collections.StringDictionary();
      }

      foreach (var itemUri in itemUris)
      {
        // fix: getting item considering the version and language from item uri
        Item item = Context.ContentDatabase.GetItem(new DataUri(itemUri));

        if (item == null)
        {
          isFailed = true;

          continue;
        }

        WorkflowState state = workflow.GetState(item);

        if (state == null)
        {
          continue;
        }

        // When the operation is performing on a single item the workflowStateId is null. We will skip the workflowStateId check.
        if (string.IsNullOrWhiteSpace(workflowStateId) || state.StateID == workflowStateId)
        {
          if (fields.Count < 1 || !fields.ContainsKey("Comments"))
          {
            string commentText = string.IsNullOrWhiteSpace(state.DisplayName) ? string.Empty : state.DisplayName;
            fields.Add("Comments", commentText);
          }

          try
          {
            if (itemUris.Count == 1)
            {
              var processor = new Processor("Workflow complete state item count", this, "WorkflowCompleteStateItemCount");
              workflow.Execute(command, item, fields, true, processor);
            }
            else
            {
              workflow.Execute(command, item, fields, true);
            }

          }
          catch (WorkflowStateMissingException)
          {
            isFailed = true;
          }
        }
      }

      if (isFailed)
      {
        SheerResponse.Alert(
          Texts.ONE_OR_MORE_ITEMS_COULD_NOT_BE_PROCESSED_AS_THEIR_WORKFLOW_STATE_DOES_NOT_SPECIFY_A_NEXT_STEP);
      }
    }
    #endregion
    #region Methods
    /// <summary>
    /// Updates the ribbon.
    /// </summary>
    private void UpdateRibbon()
    {
      var ribbon = new Ribbon
      {
        ID = "WorkboxRibbon",
        CommandContext = new CommandContext()
      };

      Item item = Context.Database.GetItem("/sitecore/content/Applications/Workbox/Ribbon");
      Error.AssertItemFound(item, "/sitecore/content/Applications/Workbox/Ribbon");

      ribbon.CommandContext.RibbonSourceUri = item.Uri;
      ribbon.CommandContext.CustomData = this.IsReload;
      this.RibbonPanel.Controls.Add(ribbon);
    }

    /// <summary>
    /// Wires the up navigators.
    /// </summary>
    /// <param name="control">
    /// The control.
    /// </param>
    private void WireUpNavigators([NotNull] System.Web.UI.Control control)
    {
      foreach (System.Web.UI.Control child in control.Controls)
      {
        var navigator = child as Navigator;

        if (navigator != null)
        {
          navigator.Jump += this.Jump;
          navigator.Previous += this.Jump;
          navigator.Next += this.Jump;
        }

        this.WireUpNavigators(child);
      }
    }

    /// <summary>
    /// Get the Workflow Provider
    /// </summary>
    /// <returns></returns>
    protected virtual IWorkflow GetWorkflowFromPage()
    {
      IWorkflowProvider workflowProvider = Context.ContentDatabase.WorkflowProvider;
      if (workflowProvider == null)
      {
        return null;
      }

      return workflowProvider.GetWorkflow(Context.ClientPage.ServerProperties["WorkflowID"] as string);
    }

    /// <summary>
    /// Workflow completion callback to refresh the counts of items in workflow states.
    /// </summary>
    /// <param name="args">The arguments for the workflow execution.</param>
    [UsedImplicitly]
    private void WorkflowCompleteStateItemCount(WorkflowPipelineArgs args)
    {
      var workflow = this.GetWorkflowFromPage();
      if (workflow == null)
      {
        return;
      }

      var workFlowItemCount = workflow.GetItemCount(args.PreviousState.StateID);
      if (this.PageSize > 0 && workFlowItemCount % this.PageSize == 0)
      {
        if (workFlowItemCount / this.PageSize > 1)
        {
          this.Offset[args.PreviousState.StateID] = this.Offset[args.PreviousState.StateID] - 1;
        }
        else
        {
          this.Offset[args.PreviousState.StateID] = 0;
        }
      }

      var urlArguments = workflow.GetStates().ToDictionary(state => state.StateID, state => this.Offset[state.StateID].ToString());
      this.Refresh(urlArguments);
    }

    #endregion

    /// <summary>
    /// Holds items for a workflow state.
    /// </summary>
    protected class StateItems
    {
      /// <summary>
      /// Gets or sets the items for the state.
      /// </summary>
      public IEnumerable<Item> Items { get; set; }

      /// <summary>
      /// Gets or sets the command IDs for the state.
      /// </summary>
      public IEnumerable<string> CommandIds { get; set; } 
    }
  }
}