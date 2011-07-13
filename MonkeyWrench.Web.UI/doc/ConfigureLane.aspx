<%@ Page Language="C#" MasterPageFile="~/Master.master" %>

<asp:Content ID="Content2" ContentPlaceHolderID="content" runat="Server">

<h2>Add a new lane</h2>
Here we're going to add a new lane, called 'mono-master', which will build mono from master.
<ul>
            <li><a href="~/EditLanes.aspx" runat="server">Add a new lane</a> called 'mono-master'</li>
            <li>Repository: this the repository of the code you want to build. You can use both git and svn repositories, and multiple repositories
                can be separated by commas. This is only used by the scheduler to fetch the revisions
                to build, which means that the code you want to checkout doesn't have to be in this repository.
                Set it to:
                <pre>
git://github.com/mono/mono.git
</pre>
            </li>
            <li>We don't want to build all of mono's history, so set min revision to:
                <pre>
b908803e39fc79982d0601e04be49c2b836f087e
</pre>
            </li>
            <li>Click Save.</li>
            <li>Now we need to create the commands you want your builder to execute. A minimal
                example would include checkout and make.</li>
            <li>Create two commands, named:
                <ul>
                    <li>checkout.sh</li>
                    <li>make.sh</li>
                </ul>
            </li>
            <li>Now you need to put contents into the commands, or put another way, create the corresponding
                files for these commands.</li>
            <li>Create two files:
                <ul>
                    <li>checkout.sh</li>
                    <li>make.sh</li>
                </ul>
            </li>
            <li>Edit each command (clicking on the name), and set their contents to:
                <ul>
                    <li>checkout.sh:
                        <pre>
#!/bin/bash -ex

# The current directory is a per-revision directory created by the builder
# which is automatically cleaned up when that revision's work has completed.
pushd .

# We don't want to clone the repository for every revision, so clone into a
# lane-specific directory (which isn't cleaned automatically, ever), and then
# just call 'git fetch' for every revision.
cd $BUILD_DATA_LANE
mkdir -p mono
cd mono
# try to fetch 
if ! git fetch; then
    # fetch failed, possibly because we haven't cloned yet, or something else
    # went wrong. Just delete everything and clone again.
    cd ..
    rm -Rf mono
    git clone $BUILD_REPOSITORY mono
    cd mono
fi
# Checkout the revision we want to build
git clean -xdf
git reset --hard $BUILD_REVISION

# Return to our per-revision directory
popd 
ln --symbolic $BUILD_DATA_LANE/mono mono
</pre>
                    </li>
                    <li>make.sh:
                        <pre>
#!/bin/bash -ex
cd mono
# Configure to build into a revision-specific install directory, so
# everything is cleaned up automatically when done.
./autogen.sh --prefix=$BUILD_INSTALL
make
make install
</pre>
                    </li>
                </ul>
             </li>
             <li>We now need to add buildbots to work for the lane, this is done in the 'Hosts' section (or <a href="NewBuildBot.aspx">add new buildbots</a> if you need to)</li>
             <li>Finally the scheduler has to be executed so that work is scheduled for the new lane, clicking "Administration -> Execute scheduler (full update)" will do just that.</li>
             <li>After a few seconds work will be scheduled for the lane, and the buildbots will start working.</li>
             <li></li>
        </ul>

    <h2 id="id_DeletionDirectives">
        Retention directives</h2>
    <div>
        Retention directives can be used to delete files according to some criteria. By default files are kept forever. All directives are executed regularily.
        <ul>
            <li>Directive: the name of the directive.</li>
            <li>Filename: the name of the file(s) to delete.</li>
            <li>Glob mode: how to match the filename specified in the directive against the name of the actual file.
                <ul>
                    <li>ShellGlob: a semi-colon separated list of shell globs.</li>
                    <li>RegExp: a regexp.</li>
                    <li>Exact: the match must be exact (even casing must match)</li></ul>
            </li>
            <li>Condition: which condition must match to delete the file
                <ul>
                    <li>AfterXDays:</li>
                    <li>AfterXBuiltRevisions:</li>
                </ul>
            </li>
            <li>X: the number to insert into the above Condition.</li>
        </ul>
        </div>
</asp:Content>
