<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Instancias.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Instancias"  %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Instancias</title>
    <meta charset="utf-8" />

    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <!-- Si preferís intranet 100% sin Internet, podés copiar bootstrap a Styles/bootstrap.min.css y usar esa ruta local.
         Por ahora, dejo tu CDN tal cual lo enviaste. -->
   <script src="Scripts/bootstrap.bundle.min.js"></script>
    <!-- <link rel="stylesheet" href="Styles/bootstrap.min.css" /> -->

    <style>
        body { padding: 12px; }
        pre.log-view { max-height: 220px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="container-fluid">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <h4 class="mb-0">Workflows – Instancias</h4>

            <div class="btn-group">
                <asp:HyperLink ID="lnkBackDef" runat="server"
                    NavigateUrl="WF_Definiciones.aspx"
                    CssClass="btn btn-sm btn-secondary">← Definiciones</asp:HyperLink>

                <asp:HyperLink ID="lnkBackTareas" runat="server"
                    CssClass="btn btn-sm btn-outline-secondary"
                    Visible="false">← Tareas</asp:HyperLink>
            </div>
        </div>


            <div class="form-inline mb-2">
                <label class="me-2">Definición:</label>
                <asp:DropDownList ID="ddlDef" runat="server" CssClass="form-control form-control-sm mr-2" AutoPostBack="true" OnSelectedIndexChanged="ddlDef_SelectedIndexChanged" />
                <asp:Button ID="btnRefrescar" runat="server" Text="Refrescar" CssClass="btn btn-sm btn-primary mr-2" OnClick="btnRefrescar_Click" />
                <!-- NUEVO: crear una instancia dummy de la definición seleccionada -->
                <asp:Button ID="btnCrearInst" runat="server" Text="Crear instancia (prueba)" CssClass="btn btn-sm btn-success" OnClick="btnCrearInst_Click" />
            </div>

            <asp:GridView ID="gvInst" runat="server"
                CssClass="table table-sm table-bordered table-striped"
                AutoGenerateColumns="False"
                AllowPaging="True"
                PageSize="20"
                DataKeyNames="Id"
                OnPageIndexChanging="gvInst_PageIndexChanging"
                OnRowCommand="gvInst_RowCommand"
                OnRowDataBound="gvInst_RowDataBound">
                <Columns>
                    <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="60px" />
                    <asp:BoundField DataField="WF_DefinicionId" HeaderText="Definición" />
                    <asp:BoundField DataField="Estado" HeaderText="Estado" />
                    <asp:BoundField DataField="FechaInicio" HeaderText="Inicio" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:BoundField DataField="FechaFin" HeaderText="Fin" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:TemplateField HeaderText="Error">
                        <ItemTemplate>
                            <asp:Label ID="lblErrorMsg" runat="server" CssClass="text-danger small"></asp:Label>
                        </ItemTemplate>
                    </asp:TemplateField>
                    <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="210px">
                        <ItemTemplate>
                            <asp:LinkButton ID="lnkVerDatos" runat="server" CommandName="VerDatos" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-info mr-1">Datos</asp:LinkButton>
                            <asp:LinkButton ID="lnkVerLog" runat="server" CommandName="VerLog" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-secondary mr-1">Log</asp:LinkButton>
                            <asp:LinkButton ID="lnkRetry" runat="server" CommandName="Reejecutar" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-warning">Re-ejecutar</asp:LinkButton>
                            <asp:LinkButton ID="lnkHistorial" runat="server"
                                CssClass="btn btn-sm btn-primary"
                                OnClientClick='<%# "wfMostrarHistorialInst(" + Eval("Id") + "); return false;" %>'>
                                Historial
                            </asp:LinkButton>
                        </ItemTemplate>
                    </asp:TemplateField>
                </Columns>
            </asp:GridView>

            <!-- panel datos / log -->
            <asp:Panel ID="pnlDetalle" runat="server" Visible="false" CssClass="mt-3">
                <h6 id="lblTituloDetalle" runat="server">Detalle</h6>
                <pre id="preDetalle" runat="server" class="log-box" Visible="false"></pre>
                <div class="card mb-3">
                  <div class="card-body">

                    <div class="row g-2 align-items-center mb-2">
                      <div class="col-md-6">
                        <input id="txtLogSearch" type="text" class="form-control" placeholder="Buscar en el log..." />
                      </div>

                      <div class="col-md-3">
                        <select id="ddlLogLevel" class="form-select">
                          <option value="">Todos los niveles</option>
                          <option value="Info">Info</option>
                          <option value="Warning">Warning</option>
                          <option value="Error">Error</option>
                          <option value="Debug">Debug</option>
                        </select>
                      </div>

                      <div class="col-md-3">
                        <div class="form-check">
                          <input class="form-check-input" type="checkbox" id="chkShowTech" />
                          <label class="form-check-label" for="chkShowTech">
                            Mostrar técnico (debug)
                          </label>
                        </div>
                      </div>
                    </div>

                    <div id="divLogList" runat="server" class="list-group"></div>

                  </div>
                </div>
            </asp:Panel>

            <!-- Modal Historial de Escalamiento (por Instancia) -->
            <div class="modal fade" id="mdlHistorialEsc" tabindex="-1" aria-hidden="true">
              <div class="modal-dialog modal-xl modal-dialog-scrollable">
                <div class="modal-content">
                  <div class="modal-header">
                    <h5 class="modal-title">Historial de escalamiento</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Cerrar"></button>
                  </div>
                  <div class="modal-body">
                    <div id="histEscLoading" class="py-3">Cargando...</div>
                    <div id="histEscError" class="alert alert-danger d-none"></div>
                    <div id="histEscBody" class="d-none"></div>
                  </div>
                  <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cerrar</button>
                  </div>
                </div>
              </div>
            </div>

        </div>

        <script type="text/javascript">
            (function () {
                function norm(s) { return (s || '').toString().toLowerCase(); }

                function applyLogFilters() {
                    var txt = document.getElementById('txtLogSearch');
                    var ddl = document.getElementById('ddlLogLevel');
                    var chk = document.getElementById('chkShowTech');

                    // Si el panel de log no está renderizado, no hacemos nada.
                    if (!txt || !ddl || !chk) return;

                    var q = norm(txt.value);
                    var lvl = (ddl.value || '').toString();
                    var showTech = chk.checked;

                    var items = document.querySelectorAll('.wf-log-item');
                    for (var i = 0; i < items.length; i++) {
                        var it = items[i];
                        var text = norm(it.getAttribute('data-text'));
                        var level = (it.getAttribute('data-level') || '');
                        var isTech = (it.getAttribute('data-tech') || '0') === '1';

                        var okQ = !q || text.indexOf(q) >= 0;
                        var okL = !lvl || level === lvl;
                        var okT = showTech ? true : !isTech;

                        it.style.display = (okQ && okL && okT) ? '' : 'none';
                    }
                }

                document.addEventListener('input', function (e) {
                    if (e.target && e.target.id === 'txtLogSearch') applyLogFilters();
                });

                document.addEventListener('change', function (e) {
                    if (!e.target) return;
                    if (e.target.id === 'ddlLogLevel' || e.target.id === 'chkShowTech') applyLogFilters();
                });

                // primera pasada (si existe panel)
                setTimeout(applyLogFilters, 0);
            })();
        </script>

    </form>

    <script type="text/javascript">        
        async function wfMostrarHistorialInst(instanciaId) {
        const modalEl = document.getElementById('mdlHistorialEsc');
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);

        const loading = document.getElementById('histEscLoading');
        const err = document.getElementById('histEscError');
        const body = document.getElementById('histEscBody');

        loading.classList.remove('d-none');
        err.classList.add('d-none');
        body.classList.add('d-none');
        body.innerHTML = '';

        modal.show();

        try {
            const url = '/Api/Generico.ashx?action=instancia.escalamiento.historial&instanciaId=' + encodeURIComponent(instanciaId);
            const res = await fetch(url, { cache: 'no-store' });
            const data = await res.json();

            if (!data || data.ok !== true) {
            throw new Error((data && data.error) ? data.error : 'Respuesta inválida');
            }

            const items = data.items || [];

            if (items.length === 0) {
            body.innerHTML = '<div class="text-muted">No hay historial de escalamiento para esta instancia.</div>';
            } else {
            // Agrupar por RootId (cadena)
            const groups = {};
            for (const it of items) {
                const k = String(it.rootId || it.id);
                if (!groups[k]) groups[k] = [];
                groups[k].push(it);
            }

            // Render
            let html = '';
            const roots = Object.keys(groups).sort((a,b)=> Number(b)-Number(a)); // últimos primero

            for (const rk of roots) {
                const arr = groups[rk];

                // ordenar por nivel y fecha
                arr.sort((a,b)=>{
                const la = a.nivel || 0, lb = b.nivel || 0;
                if (la !== lb) return la - lb;
                return String(a.fechaCreacion||'').localeCompare(String(b.fechaCreacion||''));
                });

                html += `<div class="mb-3 p-3 border rounded">
                <div class="fw-semibold mb-2">Cadena (rootId = ${escapeHtml(rk)})</div>`;

                for (const it of arr) {
                const badge = (it.estado || '').toLowerCase() === 'completada'
                    ? '<span class="badge bg-success">Completada</span>'
                    : '<span class="badge bg-warning text-dark">Pendiente</span>';

                const resu = it.resultado ? `<span class="badge bg-info text-dark ms-2">${escapeHtml(it.resultado)}</span>` : '';
                const nivel = (it.nivel != null) ? it.nivel : 0;

                html += `
                    <div class="d-flex align-items-start mb-2">
                    <div style="width:70px" class="text-muted small">Nivel ${nivel}</div>
                    <div class="flex-grow-1 border rounded p-2">
                        <div class="d-flex justify-content-between">
                        <div>
                            <span class="fw-semibold">#${it.id}</span>
                            <span class="ms-2">${badge}${resu}</span>
                            <span class="ms-2">Rol: <b>${escapeHtml(it.rolDestino||'')}</b></span>
                            ${it.origenTareaId ? `<span class="ms-2 text-muted small">OrigenTareaId: ${it.origenTareaId}</span>` : ''}
                        </div>
                        <div class="text-muted small">
                            ${escapeHtml(it.fechaCreacion||'')}${it.fechaCierre ? (' · ' + escapeHtml(it.fechaCierre)) : ''}
                        </div>
                        </div>
                        <div class="mt-1">${escapeHtml(it.titulo||'')}</div>
                        ${it.origenEscalamientoObj ? `
                        <details class="mt-2">
                            <summary class="small">origenEscalamiento</summary>
                            <pre class="small bg-light p-2 rounded mb-0">${escapeHtml(JSON.stringify(it.origenEscalamientoObj, null, 2))}</pre>
                        </details>` : ''
                        }
                    </div>
                    </div>`;
                }

                html += `</div>`;
            }

            body.innerHTML = html;
            }

            loading.classList.add('d-none');
            body.classList.remove('d-none');

        } catch (e) {
            loading.classList.add('d-none');
            err.textContent = 'Error: ' + (e.message || e);
            err.classList.remove('d-none');
        }
        }

        function escapeHtml(s) {
        return (s ?? '').toString()
            .replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;')
            .replaceAll('"','&quot;').replaceAll("'","&#039;");
        }


         </script>

</body>
</html>
