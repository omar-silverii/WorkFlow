; (function () {
    var register = window.WF_Inspector.register;
    var helpers = window.WF_Inspector.helpers;
    var el = helpers.el;
    var section = helpers.section;
    var rowButtons = helpers.rowButtons;
    var btn = helpers.btn;

    function checkbox(label, checked) {
        var wrap = el('div', 'section');
        var id = 'chk_' + Math.random().toString(36).slice(2);
        wrap.innerHTML = '<label><input type="checkbox" id="' + id + '"> ' + label + '</label>';
        var ck = wrap.querySelector('#' + id);
        ck.checked = !!checked;
        return { wrap: wrap, input: ck };
    }

    register('util.error', function (node, ctx, dom) {
        var ensurePosition = ctx.ensurePosition;
        var nodeEl = ctx.nodeEl;
        var body = dom.body;
        var title = dom.title;
        var sub = dom.sub;

        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Manejador de Error';
        if (sub) sub.textContent = node.key || '';

        var p = node.params || {};

        // === Etiqueta ===
        var inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        var sLbl = section('Etiqueta (label)', inpLbl);

        // === Checkboxes (aceptando nombres viejos y nuevos) ===
        var capChecked =
            (typeof p.capturarErrores !== 'undefined') ? p.capturarErrores :
                (typeof p.capturar !== 'undefined') ? p.capturar : false;

        var retryChecked =
            (typeof p.reintentar !== 'undefined') ? p.reintentar :
                (typeof p.volverAIntentar !== 'undefined') ? p.volverAIntentar : false;

        var notifChecked = !!p.notificar;

        var ckCap = checkbox('Capturar errores (detener propagación)', capChecked);
        var ckRetry = checkbox('Volver a intentar', retryChecked);
        var ckNotif = checkbox('Notificar', notifChecked);

        // === Mensaje (NUEVO) ===
        var txtMsg = document.createElement('textarea');
        txtMsg.className = 'input';
        txtMsg.rows = 3;
        txtMsg.placeholder = 'Mensaje de error. Ej: Fallo en prueba para instancia ${wf.instanceId}';
        txtMsg.value = p.mensaje || '';
        var sMsg = section('Mensaje', txtMsg);

        var bSave = btn('Guardar');
        var bDel = btn('Eliminar nodo');

        bSave.onclick = function () {
            node.label = inpLbl.value || node.label || 'Manejador de Error';

            node.params = {
                // nombres “nuevos”
                capturarErrores: !!ckCap.input.checked,
                reintentar: !!ckRetry.input.checked,
                notificar: !!ckNotif.input.checked,
                mensaje: txtMsg.value || ''
            };

            // compatibilidad con nombres viejos
            node.params.capturar = node.params.capturarErrores;
            node.params.volverAIntentar = node.params.reintentar;

            ensurePosition(node);

            var elNode = nodeEl(node.id);
            if (elNode) {
                var t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = function () {
            ctx.edges = ctx.edges.filter(function (e) {
                return e.from !== node.id && e.to !== node.id;
            });
            ctx.nodes = ctx.nodes.filter(function (x) {
                return x.id !== node.id;
            });
            var elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();
            ctx.drawEdges();
            ctx.select(null);
        };

        body.appendChild(sLbl);
        body.appendChild(ckCap.wrap);
        body.appendChild(ckRetry.wrap);
        body.appendChild(ckNotif.wrap);
        body.appendChild(sMsg);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
