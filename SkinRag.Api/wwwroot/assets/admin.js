(function ($, app) {
    "use strict";
    $(function () {
        var latestStatus = null,
            previousState = null,
            requestBusy = false,
            statusRequest = null,
            tick = 0;
        var lastActionError = "";
        var $dialog = $("#rebuildDialog"),
            $rebuild = $("#rebuildKnowledge");

        function eventLog(text) {
            $("#eventLog .empty-log").remove();
            $("<div>")
                .addClass("event")
                .append(
                    $("<span>").addClass("dot"),
                    $("<div>").append($("<p>").text(text), $("<time>").text(app.time())),
                )
                .prependTo("#eventLog");
            $("#eventLog .event").slice(20).remove();
        }

        function updateControls() {
            var mode = latestStatus ? latestStatus.manualRebuildMode : "disabled";
            $rebuild.prop(
                "disabled",
                requestBusy || !latestStatus || latestStatus.isRebuilding || mode === "disabled",
            );
            $("#forceRebuild").prop(
                "disabled",
                requestBusy || !!(latestStatus && latestStatus.isRebuilding),
            );
            $("#adminKeyField").prop("hidden", mode !== "api-key");
            $("#localAccessNote").prop("hidden", mode !== "local-development");
            $rebuild.find("span").text(requestBusy ? "در حال بازسازی…" : "بازسازی دانش");
            $("#rebuildHint").text(
                requestBusy
                    ? "درخواست ارسال شده است؛ پیشرفت در بخش وضعیت نمایش داده می‌شود."
                    : !latestStatus
                      ? "در حال بررسی دسترسی بازسازی…"
                      : latestStatus.isRebuilding
                        ? "یک بازسازی در حال اجراست؛ تا پایان آن منتظر بمانید."
                        : mode === "disabled"
                          ? "بازسازی دستی فعال نیست؛ تنظیم کلید مدیریت روی سرور لازم است."
                          : mode === "local-development"
                            ? "فقط اتصال محلی در محیط توسعه این دسترسی را دارد."
                            : "برای بازسازی، کلید مدیریت معتبر را وارد کنید.",
            );
        }

        function renderStatus(status) {
            latestStatus = status;
            var state = status.isRebuilding
                ? "building"
                : status.isReady
                  ? "ready"
                  : status.lastError
                    ? "error"
                    : "pending";
            var title =
                state === "building"
                    ? "دانش در حال بازسازی است"
                    : state === "ready"
                      ? "آماده پاسخ‌گویی"
                      : state === "error"
                        ? "بازسازی نیاز به بررسی دارد"
                        : "دانش هنوز آماده نیست";
            app.badge(
                $("#indexBadge"),
                state === "building"
                    ? "در حال بازسازی"
                    : state === "ready"
                      ? "آماده"
                      : state === "error"
                        ? "خطا"
                        : "در انتظار",
                state === "ready" ? "good" : state === "error" ? "bad" : "warn",
            );
            $("#statusTitle").text(title);
            $("#statusDescription").text(
                status.isRebuilding && status.isReady
                    ? "پاسخ‌گویی با دانش قبلی ادامه دارد؛ نسخه تازه پس از تکمیل جایگزین می‌شود."
                    : status.isReady
                      ? "محصول‌های فعال در دانش ثبت شده‌اند و چت در دسترس است."
                      : "پس از تکمیل اولین بازسازی، گفت‌وگو در دسترس خواهد بود.",
            );
            $("#statusHeroIcon").toggleClass("pending", state !== "ready");
            var percent = status.totalProducts
                ? Math.min(
                      100,
                      Math.max(
                          0,
                          Math.round((status.processedProducts / status.totalProducts) * 100),
                      ),
                  )
                : status.isReady
                  ? 100
                  : 0;
            $("#progressBar").css("width", percent + "%");
            $("#indexProgress").attr("aria-valuenow", percent);
            $("#progressPercent").text(app.number(percent) + "٪");
            $("#progressCaption").text(
                app.number(status.processedProducts) +
                    " از " +
                    app.number(status.totalProducts) +
                    " محصول بررسی شده",
            );
            $("#indexedProducts").text(app.number(status.indexedProducts));
            $("#cachedProducts").text(app.number(status.cachedProducts));
            $("#lastIndexed").text(app.date(status.updatedAtUtc));
            $("#embeddingModel").text(status.embeddingModel || "ثبت نشده");
            $("#chatModel").text(status.chatModel || "ثبت نشده");
            $("#indexError")
                .text(status.lastError || "")
                .prop("hidden", !status.lastError);
            $("#lastChecked").text("آخرین بررسی: " + app.time());
            if (previousState !== state) {
                eventLog(
                    title +
                        (status.isReady
                            ? " · " + app.number(status.indexedProducts) + " محصول"
                            : ""),
                );
                previousState = state;
            }
            updateControls();
        }

        function checkStatus() {
            if (statusRequest) return;
            $("#refreshStatus").prop("disabled", true);
            statusRequest = app
                .request("/api/knowledge/status")
                .done(function (status) {
                    $("#adminError").text(lastActionError).prop("hidden", !lastActionError);
                    renderStatus(status);
                })
                .fail(function (xhr) {
                    latestStatus = null;
                    app.badge($("#indexBadge"), "ارتباط قطع است", "bad");
                    $("#adminError").text(app.error(xhr)).prop("hidden", false);
                    updateControls();
                })
                .always(function () {
                    statusRequest = null;
                    $("#refreshStatus").prop("disabled", false);
                });
            app.request("/health/ready")
                .done(function (health) {
                    $("#databaseStatus").text(health.databaseReady ? "متصل" : "در دسترس نیست");
                })
                .fail(function (xhr) {
                    var health = xhr.responseJSON;
                    $("#databaseStatus").text(
                        health && health.databaseReady ? "متصل" : "در دسترس نیست",
                    );
                });
        }

        function loadStats() {
            app.request("/api/catalog/stats")
                .done(function (stats) {
                    ["totalProducts", "availableProducts", "variants", "categories"].forEach(
                        function (key) {
                            $("#" + key).text(app.number(stats[key]));
                        },
                    );
                    $("#brandsCount").text(app.number(stats.brands));
                    $("#demoCount").text(app.number(stats.demoProducts));
                    $("#activeCount").text(app.number(stats.activeProducts));
                    $("#domainStats").empty();
                    ["skin", "hair", "beauty"].forEach(function (domain) {
                        var entry = stats.domains.find(function (d) {
                            return d.domain === domain;
                        }) || { products: 0, activeProducts: 0 };
                        var percent = stats.totalProducts
                            ? Math.max(
                                  0,
                                  Math.min(100, (entry.products / stats.totalProducts) * 100),
                              )
                            : 0;
                        $("#domainStats").append(
                            $("<div>")
                                .addClass("domain-row")
                                .attr("data-domain", domain)
                                .append(
                                    $("<div>")
                                        .addClass("domain-title")
                                        .append(
                                            $("<span>").text(app.domainNames[domain]),
                                            $("<span>").text(app.number(entry.products) + " محصول"),
                                        ),
                                    $("<div>")
                                        .addClass("progress-track")
                                        .append(
                                            $("<div>")
                                                .addClass("progress-bar")
                                                .css("width", percent + "%"),
                                        ),
                                ),
                        );
                    });
                })
                .fail(function (xhr) {
                    $("#adminError")
                        .text("دریافت آمار کاتالوگ ناموفق بود. " + app.error(xhr))
                        .prop("hidden", false);
                });
        }

        $rebuild.on("click", function () {
            if (!latestStatus || requestBusy || latestStatus.isRebuilding) return;
            if (latestStatus.manualRebuildMode === "api-key" && !$("#adminKey").val().trim()) {
                app.toast("کلید مدیریت را وارد کنید.");
                $("#adminKey").trigger("focus");
                return;
            }
            $("#dialogDescription").text(
                $("#forceRebuild").prop("checked")
                    ? "تمام بردارها دوباره تولید می‌شوند و کش نادیده گرفته می‌شود. این کار زمان بیشتری نیاز دارد. محصولات و تنوع‌ها حذف نمی‌شوند."
                    : "محصول‌های فعال بررسی می‌شوند و فقط محتوای تغییرکرده بردار تازه می‌گیرد. کاتالوگ محصولات حذف یا تغییر نمی‌کند.",
            );
            $dialog[0].showModal();
        });
        $("#cancelRebuild").on("click", function () {
            $dialog[0].close();
        });
        $("#confirmRebuild").on("click", function () {
            $dialog[0].close();
            if (!latestStatus || requestBusy) return;
            var force = $("#forceRebuild").prop("checked"),
                key = $("#adminKey").val().trim();
            $("#adminKey").val("");
            requestBusy = true;
            updateControls();
            lastActionError = "";
            $("#adminError").prop("hidden", true);
            eventLog(
                force
                    ? "درخواست بازسازی کامل ارسال شد."
                    : "درخواست بازسازی با استفاده از کش ارسال شد.",
            );
            app.request("/api/knowledge/rebuild?force=" + force, {
                method: "POST",
                contentType: "application/json; charset=utf-8",
                data: "{}",
                timeout: 1200000,
                headers: { "X-Admin-Key": key, "X-Requested-With": "XMLHttpRequest" },
            })
                .done(function (status) {
                    lastActionError = "";
                    renderStatus(status);
                    eventLog(
                        "بازسازی با موفقیت تکمیل شد؛ " +
                            app.number(status.indexedProducts) +
                            " محصول آماده است.",
                    );
                    app.toast("پایگاه دانش به‌روز شد.");
                    loadStats();
                })
                .fail(function (xhr) {
                    var error = app.error(xhr);
                    lastActionError = error;
                    $("#adminError").text(error).prop("hidden", false);
                    eventLog("بازسازی انجام نشد: " + error);
                })
                .always(function () {
                    requestBusy = false;
                    updateControls();
                    checkStatus();
                });
        });
        $("#refreshStatus").on("click", function () {
            checkStatus();
            loadStats();
        });
        $(document).on("visibilitychange", function () {
            if (!document.hidden) checkStatus();
        });
        window.setInterval(function () {
            if (document.hidden) return;
            checkStatus();
            if (++tick % 12 === 0) loadStats();
        }, 5000);
        checkStatus();
        loadStats();
    });
})(jQuery, window.SkinRag);
