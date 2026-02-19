; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function opt(sel, value, text) {
        const o = document.createElement("option");
        o.value = value;
        o.textContent = text;
        sel.appendChild(o);
    }

    function setVisible(elem, vis) {
        if (!elem) return;
        elem.style.display = vis ? "" : "none";
    }

    register("control.if", (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = "";
        if (title) title.textContent = node.label || "If";
        if (sub) sub.textContent = node.key || "";

        const p = node.params || {};

        // =========================
        // Label
        // =========================
        const inpLbl = el("input", "input");
        inpLbl.value = node.label || "";
        const sLbl = section("Etiqueta (label)", inpLbl);

        // =========================
        // Campo (path) + picker
        // =========================
        const inpField = el("input", "input");
        inpField.value = p.field || "";
        inpField.placeholder = "Ej: biz.oc.empresa";

        const btnPick = btn("Elegir…");
        btnPick.style.marginTop = "6px";

        const fieldWrap = el("div");
        fieldWrap.appendChild(inpField);
        fieldWrap.appendChild(btnPick);

        const sField = section("Campo", fieldWrap);

        btnPick.onclick = () => {
            if (!window.WF_FieldPicker || typeof window.WF_FieldPicker.open !== "function") {
                alert("WF_FieldPicker no está cargado.");
                return;
            }
            window.WF_FieldPicker.open({
                ctx,
                title: "Elegir campo (Estado del Workflow)",
                onPick: (v) => {
                    inpField.value = v || "";
                    refreshSummary();
                }
            });
        };

        // =========================
        // Transform
        // =========================
        const selTransform = el("select", "input");
        opt(selTransform, "none", "Sin transformación");
        opt(selTransform, "trim", "Trim");
        opt(selTransform, "lower", "Minúsculas");
        opt(selTransform, "upper", "Mayúsculas");
        selTransform.value = p.transform || "none";
        const sTransform = section("Transform", selTransform);

        // =========================
        // Operador
        // =========================
        const selOp = el("select", "input");
        opt(selOp, "==", "Igual (=)");
        opt(selOp, "!=", "Distinto (!=)");
        opt(selOp, ">=", "Mayor o igual (>=)");
        opt(selOp, "<=", "Menor o igual (<=)");
        opt(selOp, ">", "Mayor (>)");
        opt(selOp, "<", "Menor (<)");
        opt(selOp, "contains", "Contiene");
        opt(selOp, "not_contains", "No contiene");
        opt(selOp, "starts_with", "Empieza con");
        opt(selOp, "ends_with", "Termina con");
        opt(selOp, "exists", "Existe");
        opt(selOp, "not_exists", "No existe");
        opt(selOp, "empty", "Vacío");
        opt(selOp, "not_empty", "No vacío");

        selOp.value = p.op || "==";
        const sOp = section("Operador", selOp);

        // =========================
        // Valor (multilinea)
        // =========================
        const inpVal = el("textarea", "input");
        inpVal.value = p.value || "";
        inpVal.rows = 3;
        inpVal.style.resize = "vertical";
        const sVal = section("Valor", inpVal);

        // =========================
        // Modo técnico
        // =========================
        const chkAdv = el("input", "input");
        chkAdv.type = "checkbox";
        chkAdv.checked = !!p.expression;
        const sAdvToggle = section("Modo técnico (solo admins)", chkAdv);

        const inpExpr = el("textarea", "input");
        inpExpr.value = p.expression || "";
        inpExpr.rows = 3;
        inpExpr.style.resize = "vertical";
        const sExpr = section("Expresión técnica", inpExpr);

        function refreshAdvancedVisibility() {
            setVisible(sExpr, !!chkAdv.checked);
        }

        function refreshValueVisibility() {
            const opv = selOp.value;
            const hide = (opv === "exists" || opv === "not_exists" || opv === "empty" || opv === "not_empty");
            setVisible(sVal, !hide);
        }

        // =========================
        // Summary
        // =========================
        const summaryBox = el("div");
        summaryBox.style.marginTop = "8px";
        summaryBox.style.padding = "8px";
        summaryBox.style.background = "#f5f7fa";
        summaryBox.style.border = "1px solid #d0d7de";
        summaryBox.style.borderRadius = "6px";
        summaryBox.style.fontSize = "13px";

        function buildSummary() {
            const field = (inpField.value || "").trim() || "campo";
            const op = selOp.value;
            const val = (inpVal.value || "").trim();
            const transform = selTransform.value;

            let txt = "Si " + field;

            if (transform !== "none") txt += " (" + transform + ")";

            const map = {
                "==": "es igual a",
                "!=": "es distinto de",
                ">=": "es mayor o igual a",
                "<=": "es menor o igual a",
                ">": "es mayor que",
                "<": "es menor que",
                "contains": "contiene",
                "not_contains": "NO contiene",
                "starts_with": "empieza con",
                "ends_with": "termina con",
                "exists": "existe",
                "not_exists": "NO existe",
                "empty": "está vacío",
                "not_empty": "NO está vacío"
            };

            txt += " " + (map[op] || op);

            const noVal = (op === "exists" || op === "not_exists" || op === "empty" || op === "not_empty");
            if (!noVal && val) txt += ' "' + val + '"';

            return txt;
        }

        function refreshSummary() {
            const text = buildSummary();
            summaryBox.textContent = text;

            const elNode = nodeEl(node.id);
            if (elNode) {
                const b = elNode.querySelector(".node__body");
                if (b) b.textContent = text;
            }
        }

        // =========================
        // Guardar
        // =========================
        const bSave = btn("Guardar");
        const bDel = btn("Eliminar nodo");

        bSave.onclick = () => {
            const next = {};

            if (!chkAdv.checked) {
                next.field = (inpField.value || "").trim();
                next.op = selOp.value;
                if (selTransform.value !== "none") next.transform = selTransform.value;
                if ((inpVal.value || "").trim()) next.value = (inpVal.value || "").trim();
            } else {
                next.expression = (inpExpr.value || "").trim();
            }

            node.label = inpLbl.value || node.label;
            node.params = next;

            ensurePosition(node);

            window.WF_Inspector.render({ type: "node", id: node.id }, ctx, dom);
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn("drawEdges post-save", e); }
            }, 0);
        };

        bDel.onclick = () => {
            // MUTAR arrays reales (como el inspector genérico)
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) ctx.nodes.splice(i, 1);
                }
            }

            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            ctx.drawEdges();
            ctx.select(null);
        };

        // =========================
        // Eventos UI
        // =========================
        inpField.oninput = refreshSummary;
        selTransform.onchange = refreshSummary;
        inpVal.oninput = refreshSummary;
        inpExpr.oninput = refreshSummary;

        selOp.onchange = () => {
            refreshValueVisibility();
            refreshSummary();
        };

        chkAdv.onchange = () => {
            refreshAdvancedVisibility();
            refreshSummary();
        };

        // =========================
        // Render final
        // =========================
        body.appendChild(sLbl);
        body.appendChild(sField);
        body.appendChild(sTransform);
        body.appendChild(sOp);
        body.appendChild(sVal);
        body.appendChild(summaryBox);
        body.appendChild(sAdvToggle);
        body.appendChild(sExpr);
        body.appendChild(rowButtons(bSave, bDel));

        refreshAdvancedVisibility();
        refreshValueVisibility();
        refreshSummary();
    });
})();
