<%@ Page MasterPageFile="~/Master.master" Language="C#" AutoEventWireup="true" Inherits="ViewHostHistory" Codebehind="ViewHostHistory.aspx.cs" EnableSessionState="False" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">
    <script type="text/javascript" src="ViewHostHistory.js"></script>
	<div id="hostheader" runat="server"></div>
	<div id="hosthistory" runat="server"></div>
    <a href='javascript: selectAll (true)'>Select all</a>
    <a href='javascript: selectAll (false)'>Deselect all</a>
    <a href='javascript: resetSelected ()'>Reset selected</a>
    <div id="async_status" />
</asp:Content>