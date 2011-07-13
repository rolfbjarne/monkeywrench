<%@ Page Language="C#" MasterPageFile="~/Master.master" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">
 <h2>
        How to create a new buildbot.</h2>
    <ul>
        <li>First you need a machine with some spare cycles.</li>
        
        <li>Decide where to store files, suggested example which we&#39;ll follow here:
            <pre>
~/monkeywrench/builder for the builder
~/monkeywrench/data for the data
</pre>
        </li>
        <li>Checkout the builder:
            <pre>
cd ~/monkeywrench && git clone git://github.com/mono/monkeywrench.git builder
</pre>
        </li>
        <li><a href="~/EditLanes.aspx" runat="server" >Add a lane</a> for what you want your buildbot to do (this is obviously not required
            if there already is a lane configured for what you want to do). You can find more information about lanes <a href="ConfigureLane.aspx">here</a>.</li>
        
        <li><a href="~/EditHosts.aspx" runat="server">Add a new host</a> for your machine.
            <ul>
                <li>Select the lane you just created and add it to the lanes to build for your host.</li>
                <li>At the bottom of the host configuration page there is a configuration file you need to store in ~/.config/MonkeyWrench</li>
                <li>Make sure 'make build' is executed periodically in ~/monkeywrench/builder (no harm is done if it's executed while another instance is already executing). One way is to add a crontab entry:
                <pre>
*/1 * * * * make build -C $HOME/monkeywrench/builder</pre>
                Until everything is working, you might want to run 'make build' manually, otherwise you might end up with a lot of failed work in a few minutes.</li>
                <li>The log is in /tmp/MonkeyWrench.log, if anything doesn't work, that's the first place to look for clues.</li>
            </ul>
        </li>
        <li>And now your machine should start working within a minute.</li>
    </ul>
</asp:Content>
