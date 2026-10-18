(function () {
    const key = "hmi-theme";
    const media = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;

    function stored() {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    function apply(theme) {
        document.documentElement.dataset.theme = theme;
        document.documentElement.dataset.ixColorSchema = theme;
    }

    apply(stored() || (media && media.matches ? "dark" : "light"));
    if (media) {
        media.addEventListener("change", (e) => {
            if (!stored()) {
                apply(e.matches ? "dark" : "light");
            }
        });
    }

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
