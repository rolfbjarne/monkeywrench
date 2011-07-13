<%@ Page Language="C#" MasterPageFile="~/Master.master" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">
   
    <h2>How to create a new MonkeyWrench server</h2>
    <ul>
	<li>Install postgresql-server ("zypper install postgresql-server" in OpenSuse, you need at least postgresql 8.3)</li>
        <li>Checkout MonkeyWrench (http://github.com/mono/monkeywrench) to somewhere on your machine (~/monkeywrench for instance)</li>
        <li>Configure the server in /etc/MonkeyWrench.xml:
<pre>
&lt;MonkeyWrench Version="2"&gt;
	&lt;Configuration&gt;
		&lt;WebServiceUrl&gt;http://localhost:8123/WebServices/&lt;/WebServiceUrl&gt;
		&lt;AutomaticScheduler&gt;true&lt;/AutomaticScheduler&gt;
		&lt;DatabasePort&gt;7890&lt;/DatabasePort&gt;
		&lt;DataDirectory&gt;$HOME_WITH_FULL_PATH/monkeywrench/data&lt;/DataDirectory&gt;
		&lt;DatabaseDirectory&gt;$HOME_WITH_FULL_PATH/monkeywrench/data/db&lt;/DatabaseDirectory&gt;
	&lt;/Configuration&gt;
&lt;/MonkeyWrench&gt;
</pre>
        </li>
	<li>Create the database on the server:
<pre>
cd ~/monkeywrench/scripts &amp;&amp; ./dbcontrol.sh create
</pre>
</li>
	
        <li>Start xsp to test the server:
<pre>
cd ~/monkeywrench &amp;&amp; make publish web
</pre>
</li>
	<li>You should now be able to open http://localhost:8123/ and view the newly installed MonkeyWrench server. The default administrator account is 'admin', whose password also is 'admin'.</li>
    </ul>
</asp:Content>
