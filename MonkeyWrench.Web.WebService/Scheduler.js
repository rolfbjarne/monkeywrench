function toggleRepo (id)
{
	var obj = document.getElementById (id);
	if (obj.style.display == "none") {
		obj.style.display = "block";
	} else {
		obj.style.display = "none";
	}
}

function scheduleRepo (txtID) {
    var lane_id = document.getElementById (txtID).value;
    var url = getWebServicesRoot () + "/ScheduleLane.aspx?lane_id=" + lane_id;
    var container = document.getElementById ("schedule_div");
    container.style.display = 'block';
    var element = document.getElementById ("schedule_output");
    fetchContent (url, element);
}
