window.aggregatarr = {
  setTheme: function (theme) {
    document.documentElement.dataset.theme = theme;
  },
  width: function () {
    return window.innerWidth || 0;
  },
  logout: function () {
    const form = document.createElement("form");
    form.method = "POST";
    form.action = "/logout";
    const token = document.querySelector("input[name='__RequestVerificationToken']");
    if (token) {
      const copy = token.cloneNode(true);
      form.appendChild(copy);
    }
    document.body.appendChild(form);
    form.submit();
  }
};
