
function scheduleAll () {
    var url = getWebServicesRoot () + "/ScheduleLane.aspx?lane_id=all";
    var container = document.getElementById ("schedule_div");
    container.style.display = 'block';
    var element = document.getElementById ("schedule_output");
    fetchContent (url, element);
}