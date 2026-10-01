(function ($, app) {
    "use strict";
    $(function () {
        var taxonomy = null,
            ingredientNames = new Map(),
            profileNames = new Map(),
            concernNames = new Map(),
            domain = null,
            history = [],
            conversationId = null,
            pendingRequest = null;
        var indexReady = false,
            filtersReady = false,
            busy = false,
            statusRequest = null,
            filtersRequest = null,
            statusTimer = null;
        var $question = $("#question"),
            $messages = $("#messages"),
            $conversation = $("#conversation");

        function showComposerError(text) {
            $("#composerError").text(text).prop("hidden", !text);
        }
        function scrollToEnd() {
            $conversation.scrollTop($conversation[0].scrollHeight);
        }
        function setBusy(value) {
            busy = value;
            $("#sendMessage").prop("disabled", busy);
            $("#newChat, .example-question").prop("disabled", busy);
            $("#cancelSend").prop("hidden", !busy);
            $("#sendMessage .icon").toggle(!busy);
            $("#sendMessage .spinner").remove();
            if (busy)
                $("#sendMessage").append(
                    $("<span>").addClass("spinner").attr("aria-hidden", "true"),
                );
            $("#chatForm").attr("aria-busy", String(busy));
        }

        function fillSelect(id, items, label, preserve) {
            var $select = $(id),
                selected = preserve ? $select.val() : "";
            $select.empty().append($("<option>").val("").text(label));
            items.forEach(function (item) {
                $select.append(
                    $("<option>")
                        .val(item.slug || item)
                        .text(item.name || item),
                );
            });
            if (selected) $select.val(selected);
        }

        function setDomain(value, preserve) {
            domain = value || null;
            $(".domain-choice").each(function () {
                $(this).attr("aria-pressed", String(($(this).data("domain") || null) === domain));
            });
            $("#skinField").prop("hidden", domain === "hair");
            $("#hairField").prop("hidden", !!domain && domain !== "hair");
            $("#shadeField, #finishField").prop("hidden", domain !== "beauty");
            if (!preserve) $("#category, #concern, #skinType, #hairType, #shade, #finish").val("");
            if (taxonomy) {
                fillSelect(
                    "#category",
                    taxonomy.categories.filter(function (c) {
                        return (!domain || c.domain === domain) && c.parentId != null;
                    }),
                    domain ? "همه محصولات " + app.domainNames[domain] : "همه محصولات",
                    preserve,
                );
                fillSelect(
                    "#concern",
                    taxonomy.concerns.filter(function (c) {
                        return !domain || c.domain === domain;
                    }),
                    "انتخاب نشده",
                    preserve,
                );
            }
            updateSummary();
        }

        function resetPreferences(collapsePanel) {
            $(".filters-panel select").val("");
            $("#excludedIngredients").val([]);
            $("#maxPrice").val("");
            $("#fragranceFree").prop("checked", false);
            $(".advanced-filters").prop("open", false);
            setDomain(null, false);
            if (collapsePanel) {
                $(".filters-panel").removeClass("filters-open");
                $("#toggleFilters").attr("aria-expanded", "false").text("نمایش فیلترها");
            }
        }

        function updateSummary() {
            var labels = domain ? [app.domainNames[domain]] : [];
            if ($("#category").val()) labels.push($("#category option:selected").text());
            ["#skinType", "#hairType", "#brand", "#concern", "#shade", "#finish"].forEach(
                function (id) {
                    if ($(id).val()) labels.push($(id + " option:selected").text());
                },
            );
            var budget = $("#maxPrice").val();
            if (budget && Number.isFinite(Number(budget)))
                labels.push("تا " + app.number(Number(budget)) + " ریال");
            if ($("#fragranceFree").prop("checked")) labels.push("بدون عطر");
            if (($("#excludedIngredients").val() || []).length) labels.push("حذف ترکیبات انتخابی");
            $("#filterSummary").text(
                labels.length ? labels.join(" · ") : "بدون فیلتر · همه محصولات",
            );
        }

        function loadFilters() {
            if (filtersRequest) return;
            filtersReady = false;
            filtersRequest = app
                .request("/api/catalog/filters")
                .done(function (data) {
                    taxonomy = data;
                    ingredientNames = new Map(
                        data.ingredients.map(function (item) {
                            return [item.slug, item.name];
                        }),
                    );
                    profileNames = new Map(
                        data.profiles.map(function (item) {
                            return [item.slug, item.name];
                        }),
                    );
                    concernNames = new Map(
                        data.concerns.map(function (item) {
                            return [item.slug, item.name];
                        }),
                    );
                    fillSelect(
                        "#skinType",
                        data.profiles.filter(function (p) {
                            return p.kind === "skin";
                        }),
                        "مشخص نشده",
                        true,
                    );
                    fillSelect(
                        "#hairType",
                        data.profiles.filter(function (p) {
                            return p.kind === "hair";
                        }),
                        "مشخص نشده",
                        true,
                    );
                    fillSelect("#brand", data.brands, "همه برندها", true);
                    fillSelect("#shade", data.shades, "همه رنگ‌ها", true);
                    fillSelect("#finish", data.finishes, "همه جلوه‌ها", true);
                    var selectedIngredients = $("#excludedIngredients").val() || [];
                    $("#excludedIngredients").empty();
                    data.ingredients.forEach(function (item) {
                        $("#excludedIngredients").append(
                            $("<option>").val(item.slug).text(item.name),
                        );
                    });
                    $("#excludedIngredients").val(selectedIngredients);
                    filtersReady = true;
                    setDomain(domain, true);
                    checkStatus();
                })
                .fail(function (xhr) {
                    $("#chatConnection")
                        .prop("hidden", false)
                        .removeClass("info")
                        .addClass("error");
                    $("#connectionMessage").text(
                        "دریافت ترجیحات محصول ناموفق بود. " + app.error(xhr),
                    );
                    $("#retryConnection").prop("hidden", false);
                })
                .always(function () {
                    filtersRequest = null;
                });
        }

        function checkStatus() {
            if (statusRequest) return;
            window.clearTimeout(statusTimer);
            statusRequest = app
                .request("/api/knowledge/status")
                .done(function (status) {
                    indexReady = status.isReady;
                    app.badge(
                        $("#connectionBadge"),
                        indexReady ? "آماده گفت‌وگو" : "دانش در حال آماده‌سازی",
                        indexReady ? "good" : "warn",
                    );
                    if (indexReady && filtersReady) {
                        $("#chatConnection").prop("hidden", true);
                        $("#retryConnection").prop("hidden", true);
                    } else if (filtersReady) {
                        $("#chatConnection")
                            .prop("hidden", false)
                            .removeClass("error")
                            .addClass("info");
                        $("#connectionMessage").text(
                            status.lastError ||
                                "پایگاه دانش در حال آماده‌سازی است؛ " +
                                    app.number(status.processedProducts) +
                                    " از " +
                                    app.number(status.totalProducts) +
                                    " محصول بررسی شده است.",
                        );
                        $("#retryConnection").prop("hidden", !status.lastError);
                    }
                })
                .fail(function (xhr) {
                    indexReady = false;
                    app.badge($("#connectionBadge"), "ارتباط قطع است", "bad");
                    $("#chatConnection")
                        .prop("hidden", false)
                        .removeClass("info")
                        .addClass("error");
                    $("#connectionMessage").text(app.error(xhr));
                    $("#retryConnection").prop("hidden", false);
                })
                .always(function () {
                    statusRequest = null;
                    statusTimer = window.setTimeout(
                        function () {
                            if (!document.hidden) checkStatus();
                        },
                        indexReady ? 30000 : 5000,
                    );
                });
        }

        function readRequest(question) {
            var request = {
                question: question,
                history: history.slice(-4),
                excludeIngredientSlugs: $("#excludedIngredients").val() || [],
            };
            if (domain) request.domain = domain;
            var fields = {
                categorySlug: "#category",
                brandSlug: "#brand",
                concernSlug: "#concern",
            };
            if (!domain || domain === "hair") fields.hairType = "#hairType";
            if (domain !== "hair") fields.skinType = "#skinType";
            if (domain === "beauty") {
                fields.shade = "#shade";
                fields.finish = "#finish";
            }
            Object.keys(fields).forEach(function (key) {
                var value = $(fields[key]).val();
                if (value) request[key] = value;
            });
            if ($("#fragranceFree").prop("checked")) request.fragranceFree = true;
            var budget = $("#maxPrice").val();
            if (budget !== "") {
                var amount = Number(budget);
                if (!Number.isFinite(amount) || amount < 0 || amount > 1000000000)
                    throw new Error("بودجه باید عددی بین صفر و یک میلیارد ریال باشد.");
                request.maxPrice = amount;
            }
            return request;
        }

        function hasSelectedFilters(request) {
            return [
                "domain", "categorySlug", "brandSlug", "concernSlug", "skinType", "hairType",
                "minPrice", "maxPrice", "shade", "finish", "sizeValue", "sizeUnit", "fragranceFree",
            ].some(function (key) { return request[key] !== undefined; })
                || request.excludeIngredientSlugs.length > 0;
        }

        function addMessage(role, text, options) {
            options = options || {};
            $("#welcome").prop("hidden", true);
            var $message = $("<article>")
                .addClass("message " + role)
                .toggleClass("error", !!options.error);
            var $meta = $("<div>").addClass("message-meta");
            if (role === "assistant")
                $meta.append($("<span>").addClass("avatar").append(app.icon("sparkles")));
            $meta.append(
                $("<span>").text(role === "user" ? "شما" : "دستیار مراقبت"),
                $("<span>").text("· " + app.time()),
            );
            var $bubble = $("<div>").addClass("message-bubble").text(text);
            $message.append($meta, $bubble);
            if (options.filter)
                $message.append($("<p>").addClass("message-filter").text(options.filter));
            $messages.append($message);
            scrollToEnd();
            return $message;
        }

        function productImagePlaceholder() {
            return $("<div>").addClass("product-card-image-placeholder").append(app.icon("sparkles"));
        }

        function renderProducts($message, response) {
            if (!response.products || !response.products.length) return;
            var $section = $("<section>")
                .addClass("product-section")
                .attr("aria-label", "محصول‌های مرتبط");
            $section.append(
                $("<div>")
                    .addClass("product-section-heading")
                    .append(
                        $("<span>").text(app.number(response.products.length) + " محصول مرتبط"),
                        $("<span>").text("قیمت و موجودی فعلی · ریال"),
                    ),
            );
            var $grid = $("<div>").addClass("product-grid");
            response.products.forEach(function (match) {
                var p = match.product,
                    $card = $("<article>").addClass("product-card");
                var $top = $("<div>")
                    .addClass("product-card-top")
                    .append(
                        $("<span>")
                            .addClass("pill")
                            .text(p.category || app.domainNames[p.domain] || "محصول"),
                        $("<span>")
                            .addClass("product-id")
                            .text("#" + p.id),
                    );
                var $image = p.image
                    ? $("<img>")
                          .addClass("product-card-image")
                          .attr("src", p.image)
                          .attr("alt", p.name)
                          .attr("loading", "lazy")
                          .on("error", function () { $(this).replaceWith(productImagePlaceholder()); })
                    : productImagePlaceholder();
                var $summary = $("<div>")
                    .addClass("product-summary")
                    .append(
                        $("<h3>").text(p.name),
                        $("<p>")
                            .addClass("product-brand")
                            .text(p.brand || "برند ثبت نشده"),
                        $("<div>")
                            .addClass("product-code")
                            .append(
                                $("<span>").text("کد محصول"),
                                $("<b>").attr("dir", "ltr").text(p.sku || "#" + p.id),
                            ),
                    );
                var $main = $("<div>").addClass("product-main").append($image, $summary);
                var $price = $("<div>")
                    .addClass("product-price")
                    .text(app.number(p.price))
                    .append($("<small>").text("ریال"));
                var $stock = $("<p>")
                    .addClass("product-stock")
                    .text(app.number(p.stockQuantity) + " عدد موجود در تنوع‌های مطابق درخواست");
                $card.append(
                    $top,
                    $main,
                    $("<div>").addClass("product-metrics").append($price, $stock),
                );
                var $variants = $("<div>").addClass("variant-list");
                (p.variants || []).forEach(function (v) {
                    $variants.append(
                        $("<div>")
                            .addClass("variant-row")
                            .append(
                                $("<span>").text(v.name),
                                $("<span>").text(app.number(v.price) + " ریال"),
                            ),
                    );
                });
                $card.append($variants);
                var ingredients = (p.ingredients || []).map(function (slug) {
                    return ingredientNames.get(slug) || slug;
                });
                var $details = $("<details>").append(
                    $("<summary>").text("ترکیبات و راهنمای استفاده"),
                );
                $details.append(
                    $("<p>").text(
                        "ترکیبات ثبت‌شده: " +
                            (ingredients.join("، ") || "فهرست ساختاریافته ثبت نشده است."),
                    ),
                );
                if (p.usageInstructions)
                    $details.append($("<p>").text("روش استفاده: " + p.usageInstructions));
                if (p.warnings) $details.append($("<p>").text(p.warnings));
                var facts = [];
                if (p.fragranceFree !== null && p.fragranceFree !== undefined) {
                    facts.push({
                        text: p.fragranceFree ? "بدون عطر افزوده" : "دارای عطر افزوده",
                        className: p.fragranceFree ? "fragrance-free" : "fragrance-added",
                    });
                }
                var profileFactCount = 0,
                    concernFactCount = 0;
                (p.profiles || []).slice(0, 2).forEach(function (slug) {
                    if (profileNames.has(slug)) {
                        facts.push({ text: profileNames.get(slug) });
                        profileFactCount++;
                    }
                });
                (p.concerns || []).slice(0, 3).forEach(function (slug) {
                    if (concernNames.has(slug)) {
                        facts.push({ text: concernNames.get(slug) });
                        concernFactCount++;
                    }
                });
                if (!profileFactCount) {
                    if (p.skinTypes) facts.push({ text: p.skinTypes });
                    if (p.hairTypes) facts.push({ text: p.hairTypes });
                }
                if (!concernFactCount && p.concernsText) facts.push({ text: p.concernsText });
                if (facts.length)
                    $card.append(
                        $("<div>")
                            .addClass("product-facts")
                            .append(
                                facts.map(function (fact) {
                                    return $("<span>")
                                        .addClass("product-fact " + (fact.className || ""))
                                        .text(fact.text);
                                }),
                            ),
                    );
                if (p.isDemo)
                    $card.append(
                        $("<p>")
                            .addClass("product-data-notice")
                            .text("اطلاعات نمونه است؛ قیمت و مشخصات با فروشنده تأیید نشده‌اند."),
                    );
                $card.append($details);
                $grid.append($card);
            });
            $section.append($grid);
            $message.append($section);
        }

        $("#chatForm").on("submit", function (event) {
            event.preventDefault();
            if (busy) return;
            var question = $question.val().trim(),
                request;
            showComposerError("");
            if (question.length > 2000) {
                showComposerError("پیام باید حداکثر ۲۰۰۰ نویسه باشد.");
                $question.trigger("focus");
                return;
            }
            try {
                request = readRequest(question);
                if (!question) {
                    if (!hasSelectedFilters(request)) {
                        showComposerError("ابتدا دست‌کم یک فیلتر انتخاب کنید یا پیام بنویسید.");
                        return;
                    }
                    request.filtersOnly = true;
                }
            } catch (error) {
                showComposerError(error.message);
                return;
            }
            request.conversationId = conversationId;
            addMessage("user", question || "جست‌وجو بر اساس فیلترهای انتخاب‌شده", { filter: $("#filterSummary").text() });
            var $thinking = addMessage("assistant", "");
            $thinking
                .find(".message-bubble")
                .append(
                    $("<div>")
                        .addClass("thinking")
                        .append(
                            $("<span>"),
                            $("<span>"),
                            $("<span>"),
                            $("<small>").text("در حال بررسی محصولات و آماده‌کردن پاسخ…"),
                        ),
                );
            $question.val("").trigger("input");
            setBusy(true);
            scrollToEnd();
            var started = Date.now();
            var elapsedTimer = window.setInterval(function () {
                var seconds = Math.floor((Date.now() - started) / 1000);
                $thinking
                    .find(".thinking small")
                    .text(
                        "در حال آماده‌کردن پاسخ… " +
                            app.number(seconds) +
                            " ثانیه" +
                            (seconds >= 20 ? " · می‌توانید درخواست را متوقف کنید." : ""),
                    );
            }, 1000);
            function finishRequest() {
                window.clearInterval(elapsedTimer);
                pendingRequest = null;
                setBusy(false);
                $question.trigger("focus");
            }
            pendingRequest = app
                .request("/api/consultation/ask", {
                    method: "POST",
                    contentType: "application/json; charset=utf-8",
                    data: JSON.stringify(request),
                    timeout: 230000,
                })
                .done(function (response) {
                    try {
                        if (
                            !response ||
                            typeof response.answer !== "string" ||
                            !Array.isArray(response.products)
                        )
                            throw new Error("Invalid response");
                        conversationId = response.conversationId || conversationId;
                        var cardOnlyIntents = [
                                "PRICE_INQUIRY",
                                "AVAILABILITY_INQUIRY",
                                "PRODUCT_DETAILS",
                                "PRODUCT_COMPARISON",
                            ],
                            shortCatalogAnswers = [
                                "این گزینه‌ها با نیاز و فیلترهای شما در کاتالوگ فعلی مطابقت دارند.",
                                "این محصولات در کاتالوگ فعلی برای بررسی شما انتخاب شدند.",
                            ],
                            hideAnswer =
                                response.products.length > 0 &&
                                (cardOnlyIntents.includes(response.intent) ||
                                    shortCatalogAnswers.includes(response.answer));
                        $thinking
                            .find(".message-bubble")
                            .empty()
                            .text(response.answer)
                            .prop("hidden", hideAnswer);
                        $thinking.find(".message-meta").prop("hidden", hideAnswer);
                        if (response.notice)
                            $thinking.append(
                                $("<p>")
                                    .addClass("notice info message-notice")
                                    .text(response.notice),
                            );
                        renderProducts($thinking, response);
                        history.push(
                            {
                                role: "user",
                                content: (question || "جست‌وجو بر اساس فیلترهای انتخاب‌شده").slice(0, 800),
                            },
                            { role: "assistant", content: response.answer.slice(0, 800) },
                        );
                        history = history.slice(-4);
                        scrollToEnd();
                    } catch (error) {
                        $thinking
                            .addClass("error")
                            .find(".message-bubble")
                            .empty()
                            .text("نمایش پاسخ ناموفق بود. لطفاً دوباره تلاش کنید.");
                        if (!$question.val()) $question.val(question).trigger("input");
                    } finally {
                        finishRequest();
                    }
                })
                .fail(function (xhr, state) {
                    try {
                        var text =
                            state === "abort"
                                ? "درخواست متوقف شد. می‌توانید پیام را ویرایش و دوباره ارسال کنید."
                                : state === "timeout"
                                  ? "زمان پاسخ‌گویی تمام شد. لطفاً دوباره تلاش کنید."
                                  : app.error(xhr);
                        $thinking
                            .toggleClass("error", state !== "abort")
                            .find(".message-bubble")
                            .empty()
                            .text(text);
                        if (!$question.val()) $question.val(question).trigger("input");
                        if (xhr.status === 503) checkStatus();
                        scrollToEnd();
                    } finally {
                        finishRequest();
                    }
                });
        });

        $question
            .on("keydown", function (event) {
                if (
                    event.key === "Enter" &&
                    !event.shiftKey &&
                    !(event.originalEvent && event.originalEvent.isComposing)
                ) {
                    event.preventDefault();
                    $("#chatForm").trigger("submit");
                }
            })
            .on("input", function () {
                $("#characterCount").text(app.number(this.value.length) + " / " + app.number(2000));
            });
        $("#cancelSend").on("click", function () {
            if (pendingRequest) pendingRequest.abort();
        });
        $("#newChat").on("click", function () {
            if (busy) return;
            history = [];
            conversationId = null;
            resetPreferences(true);
            $messages.empty();
            $("#welcome").prop("hidden", false);
            $question.val("").trigger("input").trigger("focus");
            showComposerError("");
            $conversation.scrollTop(0);
        });
        $(".domain-choice").on("click", function () {
            setDomain($(this).data("domain"), false);
        });
        $("#toggleFilters").on("click", function () {
            var expanded = $(this).attr("aria-expanded") !== "true";
            $(this)
                .attr("aria-expanded", String(expanded))
                .text(expanded ? "بستن فیلترها" : "نمایش فیلترها");
            $(".filters-panel").toggleClass("filters-open", expanded);
        });
        $(".filters-panel input, .filters-panel select").on("change input", updateSummary);
        $("#resetFilters").on("click", function () {
            resetPreferences(false);
        });
        $("#applyFilters").on("click", function () {
            $("#chatForm").trigger("submit");
        });
        $(".example-question").on("click", function () {
            if (busy) return;
            var type = $(this).data("example");
            if (type === "skin") {
                $question.val("برای پوست خشک یک کرم مرطوب‌کننده ارزان می‌خواهم");
            } else if (type === "hair") {
                $question.val("برای موی فر و وز یک کرم مو بدون آبکشی می‌خواهم");
            } else {
                $question.val("یک رژ لب صورتی مات می‌خواهم");
            }
            updateSummary();
            $question.trigger("input").trigger("focus");
        });
        $("#retryConnection").on("click", function () {
            if (!filtersReady) loadFilters();
            checkStatus();
        });
        $(document).on("visibilitychange", function () {
            if (!document.hidden) checkStatus();
        });
        loadFilters();
        checkStatus();
    });
})(jQuery, window.SkinRag);
