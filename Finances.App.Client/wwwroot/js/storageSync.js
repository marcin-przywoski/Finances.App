// Notifies .NET when another tab writes the finance snapshot, so this tab
// drops its cached copy instead of clobbering the newer data on its next save.
globalThis.financeStorageSync = {
  _handler: null,

  register(dotNetRef, watchedKey) {
    if (this._handler) {
      window.removeEventListener('storage', this._handler);
    }

    this._handler = (event) => {
      // key === null means localStorage.clear() was called.
      if (event.key === watchedKey || event.key === null) {
        dotNetRef.invokeMethodAsync('OnExternalStorageChange');
      }
    };

    window.addEventListener('storage', this._handler);
  }
};
