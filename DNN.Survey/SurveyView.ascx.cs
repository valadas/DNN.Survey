﻿using DNN.Modules.Survey.Components;
using DNN.Modules.Survey.Components.Controllers;
using DNN.Modules.Survey.Components.Entities;
using DNN.Modules.Survey.Controls;
using DotNetNuke.Common;
using DotNetNuke.Entities.Icons;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Entities.Modules.Actions;
using DotNetNuke.Framework.JavaScriptLibraries;
using DotNetNuke.Security;
using DotNetNuke.Security.Permissions;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Localization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;

namespace DNN.Modules.Survey
{
   public partial class SurveyView : PortalModuleBase, IActionable
   {
      #region TabModuleSettings
      protected bool ShowClosingDateMessage
      {
         get
         {
            object showClosingDateMessage = ModuleController.Instance.GetTabModule(TabModuleId).TabModuleSettings["ShowClosingDateMessage"];
            if (showClosingDateMessage == null)
               return false;
            else
               return Convert.ToBoolean(showClosingDateMessage);
         }
      }
      #endregion

      #region Private Properties
      private ModulePermissionCollection _modulePermissionCollection = null;
      private SurveysController _surveysController = null;
      private SurveyOptionsController _surveyOptionsController = null;
      private SurveyResultsController _surveyResultsController = null;
      private string _cookie;

      protected ModulePermissionCollection ModulePermissionCollection
      {
         get
         {
            if (_modulePermissionCollection == null)
               _modulePermissionCollection = ModulePermissionController.GetModulePermissions(ModuleId, TabId);
            return _modulePermissionCollection;
         }
      }

      protected SurveysController SurveysController
      {
         get
         {
            if (_surveysController == null)
               _surveysController = new SurveysController();
            return _surveysController;
         }
      }

      protected SurveyOptionsController SurveyOptionsController
      {
         get
         {
            if (_surveyOptionsController == null)
               _surveyOptionsController = new SurveyOptionsController();
            return _surveyOptionsController;
         }
      }

      protected SurveyResultsController SurveyResultsController
      {
         get
         {
            if (_surveyResultsController == null)
               _surveyResultsController = new SurveyResultsController();
            return _surveyResultsController;
         }
      }

      protected bool IsSurveyExpired
      {
         get
         {
            return ((SurveyClosingDate != DateTime.MinValue) && (SurveyClosingDate < DateTime.Now));
         }
      }

      protected bool HasVoted
      {
         get
         {
            bool hasVoted;
            if (PortalSecurity.IsInRole("Administrators"))
            {
               // Administrators may always see the survey (and may vote more than once)
               hasVoted = false;
            }
            else
            {
               if ((AuthorizedUsersOnly) || (UserId > 0))
               {
                  hasVoted = (SurveysController.HasVoted(ModuleId, UserId));
               }
               else
               {
                  hasVoted = (Request.Cookies[_cookie] != null);
               }
            }
            return hasVoted;
         }
      }

      protected bool HasViewResultsPermission
      {
         get
         {
            return ModulePermissionController.HasModulePermission(ModulePermissionCollection, ModuleSecurity.VIEW_RESULTS_PERMISSION);
         }
      }

      protected int DeleteResultsActionID { get; set; }

      protected bool AuthorizedUsersOnly
      {
         get
         {
            bool authorizedUsersOnly = true;
            ModulePermissionCollection permissions = ModulePermissionController.GetModulePermissions(ModuleId, TabId);
            foreach (ModulePermissionInfo permission in permissions)
            {
               if ((permission.PermissionCode == ModuleSecurity.PERMISSION_CODE) && (permission.PermissionKey == ModuleSecurity.PARTICIPATE_PERMISSION) && ((permission.RoleID == -1) || (permission.RoleID == -3)))
               {
                  authorizedUsersOnly = false;
                  break;
               }
            }
            return (authorizedUsersOnly);
         }
      }
      #endregion

      #region Settings
      protected DateTime SurveyClosingDate
      {
         get
         {
            object surveyClosingDate = Settings["SurveyClosingDate"];
            if (surveyClosingDate == null)
               return DateTime.MinValue;
            else
               return Convert.ToDateTime(surveyClosingDate);
         }
      }

      protected int ResultsVersion
      {
         // This is: If the results are cleared, this number increases - therefore the cookie changes and users can vote again.
         get
         {
            object resultsVersion = Settings["ResultsVersion"];
            if (resultsVersion == null)
               return 0;
            else
               return Convert.ToInt32(resultsVersion);
         }
      }
      #endregion

      #region IActionable
      public ModuleActionCollection ModuleActions
      {
         get
         {
            ModuleActionCollection actions = new ModuleActionCollection();
            // Add Question
            actions.Add(GetNextActionID(), Localization.GetString("AddQuestion.Action", LocalResourceFile), ModuleActionType.AddContent, string.Empty, string.Empty, EditUrl(), false, SecurityAccessLevel.Edit, true, false);
            actions.Add(GetNextActionID(), Localization.GetString("OrganizeQuestions.Action", LocalResourceFile), ModuleActionType.AddContent, string.Empty, IconController.IconURL("ViewStats"), EditUrl("Organize"), false, SecurityAccessLevel.Edit, true, false);
            if (HasViewResultsPermission)
            {
               // View Results
               actions.Add(GetNextActionID(), Localization.GetString("ViewResults.Action", LocalResourceFile), ModuleActionType.AddContent, string.Empty, IconController.IconURL("View"), EditUrl("SurveyResults"), false, SecurityAccessLevel.View, true, false);
            }
            DeleteResultsActionID = GetNextActionID();
            actions.Add(DeleteResultsActionID, Localization.GetString("DeleteResults.Action", LocalResourceFile), "DeleteResults", "Delete", IconController.IconURL("Delete"), string.Empty, true, SecurityAccessLevel.Edit, true, false);
            return actions;
         }
      }
      #endregion

      #region Events
      protected override void OnInit(EventArgs e)
      {
         JavaScript.RequestRegistration(CommonJs.DnnPlugins);
         _cookie = string.Format("_Module_{0}_Survey_{1}_", ModuleId, ResultsVersion);
         AddActionHandler(SurveyActions_Click);
         base.OnInit(e);
      }

      protected override void OnLoad(EventArgs e)
      {
         try
         {
            SubmitSurveyButton.ValidationGroup = string.Format("Survey-{0}-ValidationGroup", ModuleId);
            if (IsSurveyExpired)
            {
               if ((HasViewResultsPermission) && (!(PortalSecurity.IsInRole("Administrators"))))
               {
                  Response.Redirect(EditUrl("SurveyResults"), false);
               }
               else
               {
                  SurveyMessageLabel.Text = String.Format(Localization.GetString("SurveyClosed.Text", LocalResourceFile), SurveyClosingDate);
                  SurveyMessageLabel.CssClass = "dnnFormMessage dnnFormWarning";
                  SubmitSurveyButton.Visible = false;
               }
            }
            else
            {
               if (ShowClosingDateMessage)
               {
                  SurveyMessageLabel.Text = String.Format(Localization.GetString("SurveyWillClose.Text", LocalResourceFile), SurveyClosingDate);
                  SurveyMessageLabel.CssClass = "dnnFormMessage dnnFormInfo";
                  SurveyMessageLabel.Visible = true;
               }

               if ((AuthorizedUsersOnly) && (UserId < 1))
               {
                  SurveyMessageLabel.Text = Localization.GetString("MustBeSignedIn.Text", LocalResourceFile);
                  SurveyMessageLabel.CssClass = "dnnFormMessage dnnFormValidationSummary";
                  SurveyMessageLabel.Visible = true;
                  SubmitSurveyButton.Visible = false;
               }
               else
               {
                  if (HasVoted)
                  {
                     if ((HasViewResultsPermission) && (!(PortalSecurity.IsInRole("Administrators"))))
                     {
                        Response.Redirect(EditUrl("SurveyResults"), false);
                     }
                     else
                     {
                        SurveyMessageLabel.Text = String.Format(Localization.GetString("HasVoted.Text", LocalResourceFile), SurveyClosingDate);
                        SurveyMessageLabel.CssClass = "dnnFormMessage dnnFormSuccess";
                        SurveyMessageLabel.Visible = true;
                        SubmitSurveyButton.Visible = false;
                     }
                  }
                  else
                  {
                     CreateSurveyItems(SurveysController.GetAll(ModuleId));
                  }
               }
            }

            if (HasViewResultsPermission)
               ViewResultsButton.Visible = true;
            else
               ViewResultsButton.Visible = false;

            base.OnLoad(e);
         }
         catch (Exception ex)
         {
            Exceptions.ProcessModuleLoadException(this, ex);
         }
      }

      protected override void OnPreRender(EventArgs e)
      {
         if (IsEditable)
         {
            StringBuilder confirmDeleteScriptBuilder = new StringBuilder();
            confirmDeleteScriptBuilder.Append("$(document).ready(function() {\r\n");
            confirmDeleteScriptBuilder.Append(string.Format("   var deleteLink = $(\"a[href='javascript: __doPostBack(\\\\\\\'dnn$ctr{0}$ModuleActions$actionButton\\\\\\\', \\\\\\\'{1}\\\\\\\')']\");\r\n", ModuleId, DeleteResultsActionID));
            confirmDeleteScriptBuilder.Append("   deleteLink.dnnConfirm({\r\n");
            confirmDeleteScriptBuilder.Append(string.Format("      text: \"{0}\",\r\n", Localization.GetString("ConfirmResultsDelete.Text", LocalResourceFile)));
            confirmDeleteScriptBuilder.Append(string.Format("      yesText: \"{0}\",\r\n", Localization.GetString("Yes.Text")));
            confirmDeleteScriptBuilder.Append(string.Format("      noText: \"{0}\",\r\n", Localization.GetString("No.Text")));
            confirmDeleteScriptBuilder.Append(string.Format("      title: \"{0}\"\r\n", Localization.GetString("DeleteResults.Action", LocalResourceFile)));
            confirmDeleteScriptBuilder.Append("   });\r\n");
            confirmDeleteScriptBuilder.Append("});\r\n");

            if (!(Page.ClientScript.IsStartupScriptRegistered("ConfirmDeleteScript")))
               Page.ClientScript.RegisterStartupScript(GetType(), "ConfirmDeleteScript", confirmDeleteScriptBuilder.ToString(), true);
         }
         base.OnPreRender(e);
      }

      protected void ViewResultsButton_Click(object sender, EventArgs e)
      {
         Response.Redirect(EditUrl("SurveyResults"), false);
      }

      protected void SubmitSurveyButton_Click(object sender, EventArgs e)
      {
         Page.Validate(string.Format("Survey_{0}_ValidationGroup", ModuleId));
         if (Page.IsValid)
         {
            List<SurveysInfo> surveys = SurveysController.GetAll(ModuleId);
            List<SurveyResultsInfo> surveyResults = new List<SurveyResultsInfo>();

            foreach (SurveysInfo survey in surveys)
            {
               SurveyResultsInfo surveyResult;
               switch (survey.OptionType)
               {
                  case QuestionType.RadioButtons:
                     SurveyRadioButtons surveyRadioButtons = (SurveyRadioButtons)FindControl(String.Format("SurveyRadiobutton_{0}", survey.SurveyID));
                     surveyResult = new SurveyResultsInfo();
                     surveyResult.SurveyOptionID = Convert.ToInt32(surveyRadioButtons.SelectedValue);
                     surveyResult.UserID = (UserId < 1 ? (int?)null : UserId);
                     surveyResult.IPAddress = Request.ServerVariables["REMOTE_ADDR"];
                     surveyResults.Add(surveyResult);
                     break;
                  case QuestionType.CheckBoxes:
                     SurveyCheckBoxes surveyCheckBoxes = (SurveyCheckBoxes)FindControl(String.Format("SurveyCheckbox_{0}", survey.SurveyID));
                     foreach (int surveyOptionID in surveyCheckBoxes.SelectedItems)
                     {
                        surveyResult = new SurveyResultsInfo();
                        surveyResult.SurveyOptionID = surveyOptionID;
                        surveyResult.UserID = (UserId < 1 ? (int?)null : UserId);
                        surveyResult.IPAddress = Request.ServerVariables["REMOTE_ADDR"];
                        surveyResults.Add(surveyResult);
                     }
                     break;
                  case QuestionType.Text:
                     SurveyText surveyTextBox = (SurveyText)FindControl(String.Format("SurveyTextBox_{0}", survey.SurveyID));
                     surveyResult = new SurveyResultsInfo();
                     surveyResult.SurveyOptionID = surveyTextBox.SurveyOptionID;
                     surveyResult.UserID = (UserId < 1 ? (int?)null : UserId);
                     surveyResult.IPAddress = Request.ServerVariables["REMOTE_ADDR"];
                     surveyResult.TextAnswer = surveyTextBox.Text;
                     surveyResults.Add(surveyResult);
                     break;
                  default:
                     break;
               }
            }
            if (PortalSecurity.IsInRole("Administrators"))
            {
               // This is just to force the SQL Script SurveyResults_Add to add the result if the user is an administrator
               SurveyResultsController.Add(surveyResults, false);
            }
            else
            {
               SurveyResultsController.Add(surveyResults, AuthorizedUsersOnly);
            }
            HttpCookie cookie = new HttpCookie(_cookie);
            cookie.Value = "True";
            cookie.Expires = (SurveyClosingDate == DateTime.MinValue ? DateTime.MaxValue : SurveyClosingDate.AddDays(1));
            Response.AppendCookie(cookie);
            SurveyPlaceHolder.Visible = false;
            SubmitSurveyButton.Visible = false;
            if (HasViewResultsPermission)
            {
               Response.Redirect(EditUrl("ViewResults"), false);
            }
            else
            {
               SurveyMessageLabel.Text = String.Format(Localization.GetString("HasVoted.Text", LocalResourceFile), SurveyClosingDate);
               SurveyMessageLabel.CssClass = "dnnFormMessage dnnFormSuccess";
               SurveyMessageLabel.Visible = true;
            }
         }
      }
      #endregion

      #region Private Methods
      private void CreateSurveyItems(List<SurveysInfo> surveys)
      {
         foreach (SurveysInfo survey in surveys)
         {
            List<SurveyOptionsInfo> surveyOptions = SurveyOptionsController.GetAll(survey.SurveyID);
            switch (survey.OptionType)
            {
               case QuestionType.RadioButtons:
                  SurveyRadioButtons surveyRadioButtons = (SurveyRadioButtons)LoadControl(String.Format("{0}Controls/SurveyRadioButtons.ascx", ControlPath));
                  surveyRadioButtons.ID = String.Format("SurveyRadiobutton_{0}", survey.SurveyID);
                  surveyRadioButtons.Label = survey.Question;
                  surveyRadioButtons.RepeatDirection = (survey.RepeatDirection.HasValue ? survey.RepeatDirection.Value : RepeatDirection.Horizontal);
                  surveyRadioButtons.RepeatColumns = (((survey.RepeatColumns == null) || (survey.RepeatColumns <= 1)) ? 1 : survey.RepeatColumns.Value);
                  surveyRadioButtons.EditUrl = EditUrl("SurveyID", survey.SurveyID.ToString());
                  surveyRadioButtons.IsEditable = IsEditable;
                  surveyRadioButtons.ErrorMessage = string.Format(Localization.GetString("RadioButtonRequired.ErrorMessage", LocalResourceFile), survey.Question);
                  surveyRadioButtons.ValidationGroup = string.Format("Survey_{0}_ValidationGroup", ModuleId);
                  surveyRadioButtons.DataSource = surveyOptions;
                  surveyRadioButtons.DataTextField = "OptionName";
                  surveyRadioButtons.DataValueField = "SurveyOptionID";
                  surveyRadioButtons.DataBind();
                  SurveyPlaceHolder.Controls.Add(surveyRadioButtons);
                  break;
               case QuestionType.CheckBoxes:
                  SurveyCheckBoxes surveyCheckBoxes = (SurveyCheckBoxes)LoadControl(String.Format("{0}Controls/SurveyCheckBoxes.ascx", ControlPath));
                  surveyCheckBoxes.ID = String.Format("SurveyCheckbox_{0}", survey.SurveyID);
                  surveyCheckBoxes.Label = survey.Question;
                  surveyCheckBoxes.RepeatDirection = (survey.RepeatDirection.HasValue ? survey.RepeatDirection.Value : RepeatDirection.Horizontal);
                  surveyCheckBoxes.RepeatColumns = (((survey.RepeatColumns == null) || (survey.RepeatColumns <= 1)) ? 1 : survey.RepeatColumns.Value);
                  surveyCheckBoxes.EditUrl = EditUrl("SurveyID", survey.SurveyID.ToString());
                  surveyCheckBoxes.IsEditable = IsEditable;
                  surveyCheckBoxes.ErrorMessage = string.Format(Localization.GetString("CheckBoxRequired.ErrorMessage", LocalResourceFile), survey.Question);
                  surveyCheckBoxes.ValidationGroup = string.Format("Survey_{0}_ValidationGroup", ModuleId);
                  surveyCheckBoxes.DataSource = surveyOptions;
                  surveyCheckBoxes.DataTextField = "OptionName";
                  surveyCheckBoxes.DataValueField = "SurveyOptionID";
                  surveyCheckBoxes.DataBind();
                  SurveyPlaceHolder.Controls.Add(surveyCheckBoxes);
                  break;
               case QuestionType.Text:
                  SurveyText surveyTextBox = (SurveyText)LoadControl(String.Format("{0}Controls/SurveyText.ascx", ControlPath));
                  surveyTextBox.ID = String.Format("SurveyTextBox_{0}", survey.SurveyID);
                  surveyTextBox.Label = survey.Question;
                  surveyTextBox.NumberOfRows = (((survey.NumberOfRows.HasValue) && (survey.NumberOfRows.Value > 1)) ? survey.NumberOfRows.Value : 1);
                  surveyTextBox.EditUrl = EditUrl("SurveyID", survey.SurveyID.ToString());
                  surveyTextBox.IsEditable = IsEditable;
                  surveyTextBox.ErrorMessage = string.Format(Localization.GetString("TextBoxRequired.ErrorMessage", LocalResourceFile), survey.Question);
                  surveyTextBox.ValidationGroup = string.Format("Survey_{0}_ValidationGroup", ModuleId);
                  surveyTextBox.SurveyOptionID = surveyOptions[0].SurveyOptionID;
                  SurveyPlaceHolder.Controls.Add(surveyTextBox);
                  break;
               default:
                  break;
            }
         }
      }

      private void SurveyActions_Click(object sender, ActionEventArgs e)
      {
         switch (e.Action.CommandName)
         {
            case "DeleteResults":
               if (e.Action.CommandArgument == "Delete")
               {
                  SurveyResultsController.DropAll(ModuleId);
                  ModuleController.Instance.UpdateModuleSetting(ModuleId, "ResultsVersion", (ResultsVersion + 1).ToString());
               }
               break;
            default:
               break;
         }
      }
      #endregion

      protected void SubmitSurveyButton_Load(object sender, EventArgs e)
      {
         LinkButton submitSurveyButton = (LinkButton)sender;
         submitSurveyButton.ValidationGroup = string.Format("Survey_{0}_ValidationGroup", ModuleId);
      }
   }
}