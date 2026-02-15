// Scripts/Inspectors/inspector.code.script.js
(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section } = helpers;

    function ensurePosition(node) {
        if (!node.position) node.position = { x: 80, y: 80 };
    }

    register("code.script", (ctx, node, dom) => {
        const body = el("div");

        const p = node.params || node.Parameters || {};
        const curLabel = node.label || node.Label || "Script (JS)";
        const curScript = p.script || "// (deshabilitado por seguridad)\nreturn {};";

        const inpLbl = el("input", { class: "form-control form-control-sm", value: curLabel, placeholder: "Título del nodo" });
        const sLbl = section("Título", inpLbl);

        const ta = el("textarea", { class: "form-control form-control-sm", rows: 10, spellcheck: "false" });
        ta.value = curScript;
        const sScript = section("Script (JS)", ta);

        const warn = document.createElement("div");
        warn.className = "alert alert-warning py-2 small mb-0";
        warn.innerHTML = "Este nodo está <b>deshabilitado por defecto</b>. El handler <code>code.script</code> no ejecuta código arbitrario (placeholder).";
        const sWarn = section("Estado", warn);

        const btnSave = helpers.btn("Guardar");
        const btnDel = helpers.btn("Eliminar nodo");

        btnSave.onclick = () => {
            node.label = (inpLbl.value || "Script (JS)").trim();
            node.params = { script: ta.value || "" };
            ensurePosition(node);

            const nd = helpers.nodeEl(node.id);
            if (nd) {
                const t = nd.querySelector(".node__title");
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: "node", id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
        };

        btnDel.onclick = () => {
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    if (ctx.nodes[i].id === node.id) ctx.nodes.splice(i, 1);
                }
            }
            const nd = helpers.nodeEl(node.id);
            if (nd) nd.remove();
            ctx.drawEdges();
            ctx.select(null);
        };

        body.appendChild(sLbl);
        body.appendChild(sScript);
        body.appendChild(sWarn);
        body.appendChild(helpers.rowButtons(btnSave, btnDel));
    });
})();
