var repoToId = {};

function renderSchedulerData (jsonResponse)
{
	var divContainer = document.getElementById ("divContainer");

	var repositories = jsonResponse.Repositories;
	var html = "";
	for (var repo in repositories) {
		if (repo == "")
			continue;

		var id = "";
		if (!(repo in repoToId)) {
			id = (Object.keys (repoToId).length + 1).toString ();
			repoToId [repo] = id;
		} else {
			id = repoToId [repo];
		}
		var divRepoId = "divRepo" + id;
		var divRepo = document.getElementById (divRepoId);
		if (divRepo == null) {
			divRepo = document.createElement ("div");
			divRepo.id = divRepoId;
			divContainer.appendChild (divRepo);
		}
		divRepo.innerText = repo;

		var divContentId = "divContent" + id;
		var divContent = document.getElementById (divContentId);
		if (divContent == null) {
			divContent = document.createElement ("div");
			divContent.id = divContentId;
			divContent.style.paddingLeft = "20px";

			var aEnqueueSimpleId = "aEnqueueSimple" + id;
			var aEnqueueSimple = document.createElement ("a");
			aEnqueueSimple.id = aEnqueueSimpleId;
			aEnqueueSimple.href = "javascript: enqueueRepo ('" + repo + "', false);";
			aEnqueueSimple.innerText = "Schedule repo";
			divContent.appendChild (aEnqueueSimple);

			divContent.appendChild (document.createTextNode (" "));

			var aEnqueueFullId = "aEnqueueFull" + id;
			var aEnqueueFull = document.createElement ("a");
			aEnqueueFull.id = aEnqueueFullId;
			aEnqueueFull.href = "javascript: enqueueRepo ('" + repo + "', true);";
			aEnqueueFull.innerText = "(full)";
			divContent.appendChild (aEnqueueFull);

			divContainer.appendChild (divContent);
		}

		var divLanesId = "divLanes" * id;
		var divLanes = document.getElementById (divLanesId);
		if (divLanes == null) {
			divLanes = document.createElement ("div");
			divLanes.id = divLanesId;

			divContainer.appendChild (divLanes);
		}

		var lanes = respositories [repo];
		if (lanes.length == 0) {
			divLanes.innerText = "No lanes for this repository";
		} else if (lanes.length == 1) {
			divLanes.innerText = "1 lane: " + lanes [0];
		} else {
			divLanes.innerText = lanes.length.toString () + " lanes";
		}

		var divLogId = "divLog" + id;
		var divLog = document.getElementById (divLogId);
		if (divLog == null) {
			divLog = document.createElement ("div");
			divLog.id = divLogId;
			divLog.style.display = "none";

			divContainer.appendChild (divLog);
		}
	}	
	divContainer.innerHTML = html;
}

function enqueueRepo (repo, full)
{
	id = repoToId [repo];
	var url = getWebServicesRoot () + "/Schedule.aspx?repo=" + encodeURIComponent (repo) + "&forcefullupdate=" + full;
	var log = document.getElementById ("divLog" + id);
	log.style.display = "block";
	log.innerText = "hm...";
	fetchContent (url, log);
	document.getElementById ("divDebug").innerText = "Enqueued";
}

function fetchData ()
{
	var url = getWebServicesRoot () + "/Scheduler.aspx";
	fetchJson (url,
	function (status)
	{
		document.getElementById ("divDebug").innerText = status;
	},
	function (obj)
	{
		var json = JSON.parse (obj);
//		document.getElementById ("divDebug").innerText = json.toString ();// + "\n\n" + obj.toString ();
		renderSchedulerData (json);
	},
	function (req)
	{
		var divDebug = document.getElementById ("divDebug");

		if (req.status == 403) {
			divDebug.innerText = "Login required";
		} else {
			divDebug.innerText = "Something went wrong: " + req.status;
		}
	});
}

window.addEventListener ("load", fetchData, false);


