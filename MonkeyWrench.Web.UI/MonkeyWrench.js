
function editEnvironmentVariable(lane_id, host_id, old_value, id, name) {
    var new_value = prompt("Enter the new value of the environment variable", old_value);
    if (new_value != old_value && new_value != null && new_value != undefined) {
        window.location = window.location.pathname + "?host_id=" + host_id + "&lane_id=" + lane_id + "&action=editEnvironmentVariableValue&id=" + id + "&value=" + encodeURIComponent(new_value) + "&name=" + encodeURIComponent(name);
    }
}

function tryCommit(lstLanes_id, lstActions_id, txtBranch_id, txtCommit_id) {
    var lane_id = document.getElementById(lstLanes_id).value;
    var lane = document.getElementById(lstLanes_id).selectedOptions[0].innerText;
    var action = document.getElementById(lstActions_id).value;
    var branch = document.getElementById(txtBranch_id).value;
    var commit = document.getElementById(txtCommit_id).value;

    window.location = window.location.pathname + "?lane_id=" + lane_id + "&lane=" + encodeURIComponent(lane) + "&action=" + action + "&branch=" + encodeURIComponent(branch) + "&commit=" + encodeURIComponent(commit);
}