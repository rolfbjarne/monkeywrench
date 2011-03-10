function selectAll(value) {
    var obj;
    var counter = 0;

    obj = document.getElementById("entry" + counter);
    while (obj != null) {
        obj.checked = value;
        counter++;
        obj = document.getElementById("entry" + counter);
    }
}

var pending_resets = new Array();
var req = null;

function resetSelected() {
    var obj;
    var counter = 0;

    obj = document.getElementById("entry" + counter);
    while (obj != null) {
        if (obj.checked) {
            pending_resets.push(obj.value + "clearrevision");
        }
        counter++;
        obj = document.getElementById("entry" + counter);
    }
    resetNext();
}

function resetNext() {
    var req;

    if (pending_resets.length == 0) {
        setAsyncStatus("Done");
        window.location.reload();
        return;
    }

    setAsyncStatus(pending_resets.length + " left...");
    var url = pending_resets.pop();
    req = new XMLHttpRequest();
    req.onreadystatechange = function () {
        if (req.readyState == 4)
            resetNext();
    };
    req.open("GET", url);
    req.send();
}

function setAsyncStatus(text) {
    document.getElementById("async_status").innerHTML = text;
}