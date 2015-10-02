<%@ Page Language="C#" MasterPageFile="~/Master.master" AutoEventWireup="true" CodeBehind="Scheduler.aspx.cs" Inherits="MonkeyWrench.Web.UI.Scheduler" %>

<asp:Content ID="Content1" ContentPlaceHolderID="content" runat="Server" EnableViewState="False">

    <script type="text/javascript" src="MonkeyWrench.js"></script>
    <script type="text/javascript" src="Scheduler.js"></script>

    <a href="javascript:fetchData ();">Reload data </a></hr>
    <div id="divDebug"></div>
    <div id="divContainer" />
</asp:Content>
