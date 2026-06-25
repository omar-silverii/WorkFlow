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

    function isNoValueOperator(op) {
        return op === "exists" || op === "not_exists" || op === "empty" || op === "not_empty";
    }

    function fillOperatorOptions(sel, selected) {
        opt(sel, "==", "Igual (=)");
        opt(sel, "!=", "Distinto (!=)");
        opt(sel, ">=", "Mayor o igual (>=)");
        opt(sel, "<=", "Menor o igual (<=)");
        opt(sel, ">", "Mayor (>)");
        opt(sel, "<", "Menor (<)");
        opt(sel, "contains", "Contiene");
        opt(sel, "not_contains", "No contiene");
        opt(sel, "starts_with", "Empieza con");
        opt(sel, "ends_with", "Termina con");
        opt(sel, "exists", "Existe");
        opt(sel, "not_exists", "No existe");
        opt(sel, "empty", "Vacío");
        opt(sel, "not_empty", "No vacío");
        sel.value = selected || "==";
    }

    function fillTransformOptions(sel, selected) {
        opt(sel, "none", "Sin transformación");
        opt(sel, "trim", "Trim");
        opt(sel, "lower", "Minúsculas");
        opt(sel, "upper", "Mayúsculas");
        sel.value = selected || "none";
    }

    function operatorText(op) {
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
        return map[op] || op || "";
    }

    function openFieldPicker(ctx, input, refreshSummary) {
        if (!window.WF_FieldPicker || typeof window.WF_FieldPicker.open !== "function") {
            alert("WF_FieldPicker no está cargado.");
            return;
        }
        window.WF_FieldPicker.open({
            ctx,
            title: "Elegir campo (Estado del Workflow)",
            onPick: (v) => {
                input.value = v || "";
                if (refreshSummary) refreshSummary();
            }
        });
    }

    function normalizeRule(rule) {
        rule = rule || {};
        return {
            field: (rule.field || rule.fieldPath || "").trim(),
            op: (rule.op || rule.operator || "not_empty").trim(),
            value: rule.value == null ? "" : String(rule.value),
            transform: (rule.transform || "none").trim() || "none"
        };
    }

    register("control.if", (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = "";
        if (title) title.textContent = node.label || "If";
        if (sub) sub.textContent = node.key || "";

        const p = node.params || {};
        const hasRules = Array.isArray(p.rules) && p.rules.length > 0;
        const hasExpression = !!p.expression;

        // =========================
        // Label
        // =========================
        const inpLbl = el("input", "input");
        inpLbl.value = node.label || "";
        const sLbl = section("Etiqueta (label)", inpLbl);

        // =========================
        // Modo
        // =========================
        const selMode = el("select", "input");
        opt(selMode, "simple", "Simple: campo / operador / valor");
        opt(selMode, "compound", "Compuesto: varias reglas Y/O");
        opt(selMode, "expression", "Técnico: expresión avanzada");
        selMode.value = hasRules ? "compound" : (hasExpression ? "expression" : "simple");
        const sMode = section("Modo de condición", selMode);

        // =========================
        // Simple
        // =========================
        const inpField = el("input", "input");
        inpField.value = p.field || "";
        inpField.placeholder = "Ej: biz.notaCredito.total";

        const btnPick = btn("Elegir…");
        btnPick.style.marginTop = "6px";

        const fieldWrap = el("div");
        fieldWrap.appendChild(inpField);
        fieldWrap.appendChild(btnPick);

        const sField = section("Campo", fieldWrap);

        btnPick.onclick = () => openFieldPicker(ctx, inpField, refreshSummary);

        const selTransform = el("select", "input");
        fillTransformOptions(selTransform, p.transform || "none");
        const sTransform = section("Transform", selTransform);

        const selOp = el("select", "input");
        fillOperatorOptions(selOp, p.op || "==");
        const sOp = section("Operador", selOp);

        const inpVal = el("textarea", "input");
        inpVal.value = p.value || "";
        inpVal.rows = 3;
        inpVal.style.resize = "vertical";
        const sVal = section("Valor", inpVal);

        // =========================
        // Compuesto
        // =========================
        const selRulesMode = el("select", "input");
        opt(selRulesMode, "all", "Todas las reglas deben cumplirse / Y");
        opt(selRulesMode, "any", "Cualquiera de las reglas debe cumplirse / O");
        selRulesMode.value = p.rulesMode || "all";
        const sRulesMode = section("Modo compuesto", selRulesMode);

        const rulesWrap = el("div");
        rulesWrap.style.display = "grid";
        rulesWrap.style.gap = "8px";
        const sRules = section("Reglas", rulesWrap);

        const bAddRule = btn("Agregar regla");
        const sAddRule = rowButtons(bAddRule);

        function addRuleEditor(rule) {
            const r = normalizeRule(rule);
            const box = el("div");
            box.style.border = "1px solid #d0d7de";
            box.style.borderRadius = "8px";
            box.style.padding = "8px";
            box.style.background = "#f8fafc";

            const inpRuleField = el("input", "input");
            inpRuleField.value = r.field;
            inpRuleField.placeholder = "Ej: biz.notaCredito.cae";

            const bRulePick = btn("Elegir…");
            bRulePick.style.marginTop = "6px";
            const ruleFieldWrap = el("div");
            ruleFieldWrap.appendChild(inpRuleField);
            ruleFieldWrap.appendChild(bRulePick);

            const selRuleOp = el("select", "input");
            fillOperatorOptions(selRuleOp, r.op || "not_empty");

            const inpRuleVal = el("textarea", "input");
            inpRuleVal.value = r.value || "";
            inpRuleVal.rows = 2;
            inpRuleVal.style.resize = "vertical";

            const selRuleTransform = el("select", "input");
            fillTransformOptions(selRuleTransform, r.transform || "none");

            const bRemove = btn("Quitar regla");

            const sRF = section("Campo", ruleFieldWrap);
            const sRO = section("Operador", selRuleOp);
            const sRV = section("Valor", inpRuleVal);
            const sRT = section("Transform", selRuleTransform);
            const sRB = rowButtons(bRemove);

            box.appendChild(sRF);
            box.appendChild(sRO);
            box.appendChild(sRV);
            box.appendChild(sRT);
            box.appendChild(sRB);
            rulesWrap.appendChild(box);

            function refreshRuleValueVisibility() {
                setVisible(sRV, !isNoValueOperator(selRuleOp.value));
            }

            bRulePick.onclick = () => openFieldPicker(ctx, inpRuleField, refreshSummary);
            bRemove.onclick = () => {
                if (rulesWrap.children.length <= 1) {
                    inpRuleField.value = "";
                    inpRuleVal.value = "";
                } else {
                    box.remove();
                }
                refreshSummary();
            };

            inpRuleField.oninput = refreshSummary;
            inpRuleVal.oninput = refreshSummary;
            selRuleTransform.onchange = refreshSummary;
            selRuleOp.onchange = () => { refreshRuleValueVisibility(); refreshSummary(); };

            box.getRule = function () {
                const out = {
                    field: (inpRuleField.value || "").trim(),
                    op: selRuleOp.value || "not_empty"
                };
                const v = (inpRuleVal.value || "").trim();
                if (!isNoValueOperator(out.op) && v) out.value = v;
                if (selRuleTransform.value && selRuleTransform.value !== "none") out.transform = selRuleTransform.value;
                return out;
            };

            refreshRuleValueVisibility();
            return box;
        }

        function collectRules() {
            const rules = [];
            Array.prototype.forEach.call(rulesWrap.children, child => {
                if (!child || typeof child.getRule !== "function") return;
                const r = child.getRule();
                if (r.field) rules.push(r);
            });
            return rules;
        }

        (hasRules ? p.rules : [{ field: p.field || "", op: p.op || "not_empty", value: p.value || "", transform: p.transform || "none" }]).forEach(addRuleEditor);
        if (!rulesWrap.children.length) addRuleEditor({ op: "not_empty" });

        bAddRule.onclick = () => { addRuleEditor({ op: "not_empty" }); refreshSummary(); };

        // =========================
        // Expresión técnica
        // =========================
        const inpExpr = el("textarea", "input");
        inpExpr.value = p.expression || "";
        inpExpr.rows = 3;
        inpExpr.style.resize = "vertical";
        const sExpr = section("Expresión técnica", inpExpr);

        const exprHelp = el("div");
        exprHelp.textContent = "Solo para condiciones que no puedan representarse con reglas guiadas.";
        exprHelp.style.marginTop = "4px";
        exprHelp.style.fontSize = "12px";
        exprHelp.style.color = "#64748b";
        sExpr.appendChild(exprHelp);

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

        function buildSimpleSummary() {
            const field = (inpField.value || "").trim() || "campo";
            const op = selOp.value;
            const val = (inpVal.value || "").trim();
            const transform = selTransform.value;

            let txt = "Si " + field;
            if (transform !== "none") txt += " (" + transform + ")";
            txt += " " + operatorText(op);
            if (!isNoValueOperator(op) && val) txt += ' "' + val + '"';
            return txt;
        }

        function buildCompoundSummary() {
            const rules = collectRules();
            if (!rules.length) return "Si condición compuesta sin reglas";
            const parts = rules.map(r => {
                let txt = (r.field || "campo") + " " + operatorText(r.op);
                if (!isNoValueOperator(r.op) && r.value) txt += ' "' + r.value + '"';
                return txt;
            });
            return "Si " + (selRulesMode.value === "any" ? "cualquiera" : "todas") + " de estas reglas: " + parts.join("; ");
        }

        function buildSummary() {
            if (selMode.value === "compound") return buildCompoundSummary();
            if (selMode.value === "expression") return "Si " + ((inpExpr.value || "").trim() || "expresión técnica");
            return buildSimpleSummary();
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

        function refreshModeVisibility() {
            const mode = selMode.value;
            const isSimple = mode === "simple";
            const isCompound = mode === "compound";
            const isExpression = mode === "expression";

            setVisible(sField, isSimple);
            setVisible(sTransform, isSimple);
            setVisible(sOp, isSimple);
            setVisible(sVal, isSimple && !isNoValueOperator(selOp.value));

            setVisible(sRulesMode, isCompound);
            setVisible(sRules, isCompound);
            setVisible(sAddRule, isCompound);

            setVisible(sExpr, isExpression);
            refreshSummary();
        }

        // =========================
        // Guardar
        // =========================
        const bSave = btn("Guardar");
        const bDel = btn("Eliminar nodo");

        bSave.onclick = () => {
            const next = {};

            if (selMode.value === "compound") {
                const rules = collectRules();
                if (!rules.length) { alert("La condición compuesta debe tener al menos una regla con campo."); return; }
                next.rulesMode = selRulesMode.value || "all";
                next.rules = rules;
            } else if (selMode.value === "expression") {
                next.expression = (inpExpr.value || "").trim();
            } else {
                next.field = (inpField.value || "").trim();
                next.op = selOp.value;
                if (selTransform.value !== "none") next.transform = selTransform.value;
                if ((inpVal.value || "").trim() && !isNoValueOperator(selOp.value)) next.value = (inpVal.value || "").trim();
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
        selRulesMode.onchange = refreshSummary;

        selOp.onchange = () => {
            refreshModeVisibility();
            refreshSummary();
        };

        selMode.onchange = refreshModeVisibility;

        // =========================
        // Render final
        // =========================
        body.appendChild(sLbl);
        body.appendChild(sMode);
        body.appendChild(sField);
        body.appendChild(sTransform);
        body.appendChild(sOp);
        body.appendChild(sVal);
        body.appendChild(sRulesMode);
        body.appendChild(sRules);
        body.appendChild(sAddRule);
        body.appendChild(sExpr);
        body.appendChild(summaryBox);
        body.appendChild(rowButtons(bSave, bDel));

        refreshModeVisibility();
        refreshSummary();
    });
})();
