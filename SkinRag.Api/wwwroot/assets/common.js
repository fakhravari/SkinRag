(function ($) {
    "use strict";
    var numberFormat = new Intl.NumberFormat("fa-IR");
    var domainNames = {
        skin: "پوست",
        hair: "مو",
        beauty: "زیبایی",
        "personal-care": "بهداشتی و مراقبتی",
        fragrance: "خوشبو کننده",
        cellulose: "سلولزی",
        promotional: "کالای تبلیغاتی",
        food: "خوراکی",
        other: "محصولات دیگر",
        bundles: "بسته های ترکیبی",
        campaigns: "جشنواره",
    };
    window.SkinRag = {
        domainNames: domainNames,
        number: function (value) {
            return value == null ? "—" : numberFormat.format(value);
        },
        date: function (value) {
            if (!value) return "هنوز بازسازی نشده";
            var date = new Date(value);
            return isNaN(date.getTime())
                ? "—"
                : date.toLocaleString("fa-IR", {
                      dateStyle: "short",
                      hour: "2-digit",
                      minute: "2-digit",
                      second: "2-digit",
                  });
        },
        time: function () {
            return new Date().toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" });
        },
        icon: function (name) {
            return $(document.createElementNS("http://www.w3.org/2000/svg", "svg"))
                .attr({ class: "icon", "aria-hidden": "true" })
                .append(
                    $(document.createElementNS("http://www.w3.org/2000/svg", "use")).attr(
                        "href",
                        "/assets/icons.svg#" + name,
                    ),
                );
        },
        request: function (url, options) {
            return $.ajax(
                $.extend(
                    { url: url, method: "GET", dataType: "json", timeout: 30000, cache: false },
                    options,
                ),
            );
        },
        error: function (xhr) {
            var data = xhr.responseJSON;
            if (data && data.errors) {
                return Object.values(data.errors).flat().slice(0, 4).join(" ");
            }
            if (data && data.title) return data.title;
            if (xhr.status === 429)
                return "تعداد درخواست‌ها زیاد است؛ یک دقیقه بعد دوباره تلاش کنید.";
            if (xhr.status === 401) return "کلید مدیریت معتبر نیست یا دسترسی مجاز نیست.";
            if (xhr.status === 0)
                return "ارتباط با سرور برقرار نشد؛ اتصال و اجرای برنامه را بررسی کنید.";
            if (xhr.status === 504) return "پاسخ مدل طول کشید؛ دوباره تلاش کنید.";
            return "درخواست انجام نشد؛ لطفاً دوباره تلاش کنید.";
        },
        badge: function ($element, text, kind) {
            $element
                .removeClass("good warn bad")
                .addClass(kind || "")
                .empty()
                .append($("<span>").addClass("dot"), $("<span>").text(text));
        },
        toast: function (text) {
            $(".toast").remove();
            var $toast = $("<div>")
                .addClass("toast")
                .attr("role", "status")
                .text(text)
                .appendTo("body");
            window.setTimeout(function () {
                $toast.remove();
            }, 4000);
        },
    };
})(jQuery);
