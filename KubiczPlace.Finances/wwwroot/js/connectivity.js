export function registerConnectivityHandler(dotNetObj) {
    function notify() {
        dotNetObj.invokeMethodAsync('SetOnlineStatus', navigator.onLine);
    }

    window.addEventListener('online', notify);
    window.addEventListener('offline', notify);

    // initial status
    notify();
}
