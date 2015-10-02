
function getSiteRoot ()
{
	var url = window.location.pathname;
	url = url.substring (1, url.lastIndexOf ('/'));
	return url;
}
   
function getWebServicesRoot ()
{
    return document.getElementById ("idWebServicesUrl").value;
}

function fetchContent (url, targetElement) {
	var req = new XMLHttpRequest ();

	targetElement.innerText = "Request in progress... (" + url + ")";

	var updated = true;
	var interval = window.setInterval (function()
	{
		if (updated) {
			updated = false;
			targetElement.innerText = req.responseText;
			if (req.readyState == 3)
				targetElement.innerText = targetElement.innerText  + "\n <waiting>";
			else if (req.readyState == 4)
				window.clearInterval (interval);

		}
	}, 1000);

	req.onreadystatechange = function() {
		if (req.readyState == 4 || req.readyState == 3) {
			if (req.status == 200) {
				updated = true;
			} else {
				targetElement.innerText = "Something went wrong. Status: " + req.status + " ReadyState: " + req.readyState + " StatusText: " + req.statusText + " ResponseText: " + req.responseText;
			}
		}
	}
	req.open("GET", url, true);
	req.send();
}

function fetchJson (url, statusCallback, completedCallback, errorCallback) {
	var req = new XMLHttpRequest ();

	statusCallback ("Request in progress... (" + url + ")");

	req.onreadystatechange = function() {
		if (req.readyState == 4) {
			if (req.status == 200) {
				completedCallback (req.response);
			} else {
				errorCallback (req);
			}
		}
	}
//	req.responseType = "json";
	req.open("GET", url, true);
	req.send();
}

function editEnvironmentVariable(lane_id, host_id, old_value, id, name) {
	var new_value = prompt("Enter the new value of the environment variable", old_value);
	if (new_value != old_value && new_value != null && new_value != undefined) {
		window.location = window.location.pathname + "?host_id=" + host_id + "&lane_id=" + lane_id + "&action=editEnvironmentVariableValue&id=" + id + "&value=" + encodeURIComponent(new_value) + "&name=" + encodeURIComponent(name);
	}
}
