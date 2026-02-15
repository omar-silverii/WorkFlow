// Scripts/Inspectors/inspector.code.function.js
(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section } = helpers;

    function ensurePosition(node) {
        if (!node.position) node.position = { x: 80, y: 80 };
    }

    register("code.function", (ctx, node, dom) => {
        const body = el("div");

        const p = node.params || node.Parameters || {};
        const curLabel = node.label || node.Label || "Función (C#)";
        const curName = p.name || "";
        const curOutput = p.output || "biz.code.function";
        const curArgs = p.args || { parts: ["hola", "mundo"], sep: " " };

        const inpLbl = el("input", { class: "form-control form-control-sm", value: curLabel, placeholder: "Título del nodo" });
        const sLbl = section("Título", inpLbl);

        const inpName = el("input", { class: "form-control form-control-sm", value: curName, placeholder: "Ej: string.concat / math.sum / json.pick" });
        const sName = section("Función (name)", inpName);

        const inpOut = el("input", { class: "form-control form-control-sm", value: curOutput, placeholder: "Ej: biz.miSalida" });
        const sOut = section("Salida (output)", inpOut);

        const ta = el("textarea", { class: "form-control form-control-sm", rows: 8, spellcheck: "false" });
        ta.value = JSON.stringify(curArgs, null, 2);
        const sArgs = section("Args (JSON)", ta);

        const info = document.createElement("div");
        info.className = "text-muted small";
        info.innerHTML = `
            <div><b>Ejemplos:</b></div>
            <div><code>string.concat</code> args: <code>{"parts":["A","B"],"sep":" "}</code></div>
            <div><code>math.sum</code> args: <code>{"values":[1,2,3]}</code></div>
            <div><code>json.pick</code> args: <code>{"from": {...}, "path": "a.b"}</code></div>
        `;
        const sInfo = section("Información", info);

        const btnSave = helpers.btn("Guardar");
        const btnDel = helpers.btn("Eliminar nodo");

        btnSave.onclick = () => {
            node.label = (inpLbl.value || "Función (C#)").trim();

            let argsObj = {};
            try { argsObj = JSON.parse(ta.value || "{}"); } catch { argsObj = {}; }

            node.params = {
                name: (inpName.value || "").trim(),
                output: (inpOut.value || "").trim(),
                args: argsObj
            };

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
        body.appendChild(sName);
        body.appendChild(sOut);
        body.appendChild(sArgs);
        body.appendChild(sInfo);
        body.appendChild(helpers.rowButtons(btnSave, btnDel));
    });
})();
