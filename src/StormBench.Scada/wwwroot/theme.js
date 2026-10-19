(function () {
    const key = "hmi-theme";

    function stored() {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    function apply(theme) {
        document.documentElement.dataset.theme = theme;
    }

    apply(stored() === "dark" ? "dark" : "light");

    window.hmiTheme = {
        current: () => document.documentElement.dataset.theme,
        toggle: () => {
            const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
            apply(next);
            try {
                localStorage.setItem(key, next);
            } catch {
            }
            return next;
        },
    };
})();
