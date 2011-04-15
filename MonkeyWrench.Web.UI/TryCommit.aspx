<%@ Page Language="C#" MasterPageFile="~/Master.master" AutoEventWireup="true" Inherits="TryCommit" CodeBehind="TryCommit.aspx.cs" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">
    <script type="text/javascript" src="MonkeyWrench.js"></script>
    <h2>
        Try Commit
    </h2>
    <div>
        <asp:Table runat="server" ID="tblMain">
            <asp:TableRow>
                <asp:TableCell>
                    <asp:Label runat="server">Lane:</asp:Label>
                </asp:TableCell>
                <asp:TableCell>
                    <asp:DropDownList ID="lstLanes" runat="server" />
                </asp:TableCell>
            </asp:TableRow>
            <asp:TableRow>
                <asp:TableCell>
            <asp:Label runat="server">Commit:</asp:Label>
                </asp:TableCell>
                <asp:TableCell>
                    <asp:TextBox ID="txtCommit" runat="server"></asp:TextBox>
                </asp:TableCell>
            </asp:TableRow>
            <asp:TableRow>
                <asp:TableCell>
                    <asp:Label runat="server" Text="Action when successful:"></asp:Label>
                </asp:TableCell>
                <asp:TableCell>
                    <asp:DropDownList ID="lstActions" runat="server">
                        <asp:ListItem Text="None" Value="0" />
                        <asp:ListItem Text="Cherry-pick" Value="1" Selected="True" />
                        <asp:ListItem Text="Merge" Value="2" />
                    </asp:DropDownList>
                </asp:TableCell>
            </asp:TableRow>
            <asp:TableRow>
                <asp:TableCell>
                    <asp:Label ID="lblBranch" runat="server" Text="Branch to cherry-pick/merge onto:"></asp:Label>
                </asp:TableCell>
                <asp:TableCell>
                    <asp:TextBox ID="txtBranch" runat="server" Text="master" />
                </asp:TableCell>
            </asp:TableRow>
            <asp:TableRow>
                <asp:TableCell ColumnSpan="2">
                    <asp:Label ID="lblMessage" ForeColor="Red" runat="server" />
                </asp:TableCell>
            </asp:TableRow>
            <asp:TableRow>
                <asp:TableCell ColumnSpan="2">
                    <input id="cmdTry" type="button" runat="server" value="Try!" />
                </asp:TableCell>
            </asp:TableRow>
        </asp:Table>
    </div>
</asp:Content>








