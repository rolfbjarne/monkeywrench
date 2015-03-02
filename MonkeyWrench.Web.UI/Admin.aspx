<%@ Page Language="C#" MasterPageFile="~/Master.master" AutoEventWireup="true" CodeBehind="Admin.aspx.cs" Inherits="Admin" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">

    <script type="text/javascript" src="MonkeyWrench.js"></script>
    <script type="text/javascript" src="Admin.js"></script>

    <div style="text-align: center; padding: 5px">
        Scheduler status:
        <button type="button" id="cmdSchedule" onclick="javascript:scheduleAll ();">Execute scheduler</button>
        <br />
       	<div id="schedule_div" style="display: none;">
		<pre id="schedule_output" style="text-align: left;">scheduleoutput</pre>
		</div>
    </div>
</asp:Content>
