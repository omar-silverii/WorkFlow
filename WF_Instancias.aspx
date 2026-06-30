<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Instancias.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Instancias"  %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Instancias</title>
    <meta charset="utf-8" />

    <link href="Content/bootstrap.min.css" rel="stylesheet" />

    <style>
        body { padding: 12px; background: #f6f7fb; }
        pre.log-view { height: 320px; min-height: 180px; max-height: 75vh; resize: vertical; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }
        .ws-resize-hint { font-size: .72rem; color: rgba(0,0,0,.48); }

        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(16,24,40,.06); border-radius: 16px; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.08); color: #0d6efd; font-size: .78rem; font-weight: 600; }
        .ws-chip.ws-chip-muted { background: rgba(108,117,125,.10); color: #6c757d; }
        .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table> :not(caption)>*>* { vertical-align: middle; }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }


        .ws-def-picker { position: relative; }
        .ws-def-input-wrap { position: relative; }
        .ws-def-input { padding-left: 34px; padding-right: 58px; }
        .ws-def-icon { position: absolute; left: 11px; top: 50%; transform: translateY(-50%); color: rgba(0,0,0,.45); pointer-events: none; font-size: .95rem; }
        .ws-def-clear { position: absolute; right: 34px; top: 50%; transform: translateY(-50%); border: 0; background: transparent; color: rgba(0,0,0,.48); line-height: 1; padding: 0 4px; }
        .ws-def-caret { position: absolute; right: 12px; top: 50%; transform: translateY(-50%); color: rgba(0,0,0,.48); pointer-events: none; font-size: .85rem; }
        .ws-def-results { display: none; position: absolute; z-index: 1050; left: 0; right: 0; top: calc(100% + 4px); max-height: 360px; overflow-y: auto; background: #fff; border: 1px solid rgba(16,24,40,.12); border-radius: 12px; box-shadow: 0 16px 34px rgba(16,24,40,.16); }
        .ws-def-results.show { display: block; }
        .ws-def-item { width: 100%; border: 0; background: #fff; text-align: left; padding: 10px 12px; border-bottom: 1px solid rgba(16,24,40,.08); display: grid; grid-template-columns: minmax(160px, 220px) minmax(220px, 1fr) auto; gap: 12px; align-items: center; }
        .ws-def-item:hover, .ws-def-item:focus { background: rgba(13,110,253,.06); outline: none; }
        .ws-def-code { font-weight: 700; color: #111827; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .ws-def-name { color: #374151; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .ws-def-meta { text-align: right; min-width: 118px; }
        .ws-def-badge { display: inline-flex; align-items: center; padding: 3px 8px; border-radius: 999px; font-size: .72rem; font-weight: 700; background: rgba(13,110,253,.10); color: #0d6efd; }
        .ws-def-badge-empty { background: rgba(108,117,125,.12); color: #6c757d; }
        .ws-def-date { display: block; margin-top: 3px; color: rgba(0,0,0,.55); font-size: .72rem; }
        .ws-def-empty { padding: 12px; color: rgba(0,0,0,.55); font-size: .86rem; display: none; }
        .ws-def-empty.show { display: block; }
        .ws-def-more { padding: 8px 12px; color: #0d6efd; font-size: .82rem; border-top: 1px solid rgba(16,24,40,.08); }
        @media (max-width: 768px) {
            .ws-def-item { grid-template-columns: 1fr; gap: 3px; }
            .ws-def-meta { text-align: left; }
        }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <!-- Topbar coherente -->
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="container-fluid">

            <div class="ws-topbar p-3 mb-3 ws-card">
                <div class="d-flex align-items-center justify-content-between">
                    <div>
                        <div class="ws-title">Instancias</div>
                        <div class="ws-muted small">Seguimiento, datos y logs</div>
                    </div>

                    <div class="d-flex gap-2">
                        <asp:HyperLink ID="lnkBackTareas" runat="server" Visible="false" CssClass="btn btn-sm btn-outline-secondary">
                            Volver a tareas
                        </asp:HyperLink>
                    </div>
                </div>
            </div>

            <div class="card ws-card mb-3">
                <div class="card-body py-3">
                    <div class="row g-2 align-items-end">
                        <div class="col-md-4 col-lg-3">
                            <label class="form-label mb-0">Abrir expediente por instancia</label>
                            <asp:TextBox ID="txtInstanciaMapa" runat="server"
                                CssClass="form-control form-control-sm"
                                placeholder="Ej: 170508" />
                        </div>
                        <div class="col-md-auto d-grid">
                            <asp:Button ID="btnAbrirMapa" runat="server"
                                Text="Abrir mapa"
                                CssClass="btn btn-sm btn-outline-dark"
                                OnClick="btnAbrirMapa_Click" />
                        </div>
                        <div class="col-md">
                            <div class="ws-muted small mb-1">Usalo cuando ya tenés el número de instancia y no sabés a qué definición pertenece.</div>
                            <asp:Label ID="lblAbrirMapaMsg" runat="server" CssClass="small text-danger" EnableViewState="false" />
                        </div>
                    </div>
                </div>
            </div>

            <div class="row g-2 align-items-end mb-2">
                <div class="col-md-4">
                    <label class="form-label mb-0">Definición:</label>
                    <asp:HiddenField ID="hidDefId" runat="server" />
                    <div class="ws-def-picker" id="defPicker">
                        <div class="ws-def-input-wrap">
                            <span class="ws-def-icon">&#128269;</span>
                            <asp:TextBox ID="txtDef" runat="server"
                                CssClass="form-control form-control-sm ws-def-input"
                                AutoPostBack="true"
                                OnTextChanged="txtDef_TextChanged"
                                autocomplete="off"
                                placeholder="Buscá por código o nombre de la definición..." />
                            <button type="button" class="ws-def-clear" id="btnDefClear" title="Limpiar definición">&times;</button>
                            <span class="ws-def-caret">&#9662;</span>
                        </div>
                        <div class="ws-def-results" id="defResults" role="listbox" aria-label="Definiciones">
                            <asp:Literal ID="litDefResults" runat="server" />
                            <div class="ws-def-empty" id="defEmpty">No se encontraron definiciones.</div>
                            <div class="ws-def-more" id="defMore">Escribí para filtrar por código, nombre o parte del texto.</div>
                        </div>
                    </div>
                    <div class="ws-muted small mt-1">Buscá por código, nombre o parte del texto. La lista muestra la última ejecución si existe.</div>
                    <asp:Label ID="lblDefMsg" runat="server" CssClass="small text-danger" EnableViewState="false" />
                </div>

                <div class="col-md-2">
                    <label class="form-label mb-0">Estado:</label>
                    <div class="btn-group btn-group-sm w-100 mb-1" role="group" aria-label="Filtro de estado">
                        <asp:LinkButton ID="lnkEstadoTodos" runat="server" CssClass="btn btn-outline-secondary" OnClick="lnkEstado_Click" CommandArgument="">Todos</asp:LinkButton>
                        <asp:LinkButton ID="lnkEstadoEnCurso" runat="server" CssClass="btn btn-outline-primary" OnClick="lnkEstado_Click" CommandArgument="EnCurso">En curso</asp:LinkButton>
                        <asp:LinkButton ID="lnkEstadoError" runat="server" CssClass="btn btn-outline-danger" OnClick="lnkEstado_Click" CommandArgument="Error">Error</asp:LinkButton>
                        <asp:LinkButton ID="lnkEstadoFinalizado" runat="server" CssClass="btn btn-outline-success" OnClick="lnkEstado_Click" CommandArgument="Finalizado">Finalizado</asp:LinkButton>
                    </div>
                    <asp:DropDownList ID="ddlEstado" runat="server" CssClass="form-select form-select-sm"
                        AutoPostBack="true" OnSelectedIndexChanged="ddlEstado_SelectedIndexChanged">
                        <asp:ListItem Text="(Todos)" Value="" />
                        <asp:ListItem Text="EnCurso" Value="EnCurso" />
                        <asp:ListItem Text="Error" Value="Error" />
                        <asp:ListItem Text="Finalizado" Value="Finalizado" />
                    </asp:DropDownList>
                </div>

                <div class="col-md-3">
                    <label class="form-label mb-0">Buscar dentro de la definición:</label>
                    <asp:TextBox ID="txtBuscar" runat="server"
                        CssClass="form-control form-control-sm"
                        placeholder="Id / texto dentro de esta definición" />
                </div>

                <div class="col-md-2">
                    <div class="form-check mt-3">
                        <asp:CheckBox ID="chkMostrarFinalizados" runat="server"
                            CssClass="form-check-input"
                            AutoPostBack="true"
                            OnCheckedChanged="chkMostrarFinalizados_CheckedChanged" />
                        <label class="form-check-label" for="chkMostrarFinalizados">
                            Mostrar finalizados
                        </label>
                    </div>
                </div>

                <div class="col-md-1 d-grid">
                    <asp:Button ID="btnRefrescar" runat="server"
                        Text="Refrescar"
                        CssClass="btn btn-sm btn-primary"
                        OnClick="btnRefrescar_Click" />
                </div>

                <div class="col-md-12 d-flex gap-2">
                    <asp:Button ID="btnBuscar" runat="server"
                        Text="Buscar"
                        CssClass="btn btn-sm btn-outline-primary"
                        OnClick="btnBuscar_Click" />
                    <asp:Button ID="btnCrearInst" runat="server"
                        Text="Ejecutar (sin input)"
                        CssClass="btn btn-sm btn-outline-success"
                        OnClick="btnCrearInst_Click" />
                </div>
            </div>

            <div class="row g-3">
                <!-- IZQUIERDA: listado -->
                <div class="col-md-7">
                    <div class="card ws-card">
                        <div class="card-body">
                            <div class="d-flex align-items-center justify-content-between mb-2">
                                <div>
                                    <div class="ws-title">Listado</div>
                                    <div class="ws-muted small">TOP 500</div>
                                </div>
                                <div>
                                    <span class="ws-chip ws-chip-muted">TOP 500</span>
                                </div>
                            </div>

                            <asp:GridView ID="gvInst" runat="server" CssClass="table table-sm table-hover ws-grid"
                                AutoGenerateColumns="false" GridLines="None"
                                AllowPaging="true" PageSize="20"
                                OnPageIndexChanging="gvInst_PageIndexChanging"
                                OnRowCommand="gvInst_RowCommand">
                                <Columns>
                                    <asp:BoundField DataField="Id" HeaderText="Id" />
                                    <asp:BoundField DataField="Estado" HeaderText="Estado" />
                                    <asp:BoundField DataField="FechaInicio" HeaderText="Inicio" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" />
                                    <asp:BoundField DataField="FechaFin" HeaderText="Fin" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" />

                                    <asp:TemplateField HeaderText="Acciones">
                                        <ItemTemplate>
                                            <asp:HyperLink runat="server" CssClass="btn btn-sm btn-outline-dark"
                                                NavigateUrl='<%# "WF_Instancia_Mapa.aspx?id=" + Eval("Id") %>'>Mapa</asp:HyperLink>
                                            <asp:LinkButton runat="server" CssClass="btn btn-sm btn-outline-primary"
                                                CommandName="Datos" CommandArgument='<%# Eval("Id") %>'>Datos</asp:LinkButton>
                                            <asp:LinkButton runat="server" CssClass="btn btn-sm btn-outline-secondary"
                                                CommandName="Logs" CommandArgument='<%# Eval("Id") %>'>Logs</asp:LinkButton>
                                            <asp:LinkButton runat="server" CssClass="btn btn-sm btn-outline-success"
                                                CommandName="Docs" CommandArgument='<%# Eval("Id") %>'>Docs</asp:LinkButton>
                                        </ItemTemplate>
                                    </asp:TemplateField>
                                </Columns>
                            </asp:GridView>

                        </div>
                    </div>
                </div>

                <!-- DERECHA: detalles -->
                <div class="col-md-5">

                    <!-- DatosContexto -->
                    <asp:Panel ID="pnlDatosCard" runat="server" Visible="true">
                        <div class="card ws-card mb-3">
                            <div class="card-body">
                                <div class="d-flex align-items-center justify-content-between mb-2 gap-2">
                                    <div class="ws-title">Datos (DatosContexto)</div>
                                    <div class="ws-resize-hint text-end">Arrastrá la esquina inferior derecha para agrandar</div>
                                </div>

                                <asp:Panel ID="pnlDatos" runat="server" Visible="false">
                                    <pre class="log-view"><asp:Literal ID="litDatos" runat="server"></asp:Literal></pre>
                                </asp:Panel>

                                <asp:Panel ID="pnlDatosEmpty" runat="server" Visible="true" CssClass="ws-muted small">
                                    Seleccioná una instancia y presioná “Datos”.
                                </asp:Panel>
                            </div>
                        </div>
                    </asp:Panel>

                    <!-- Documentos (Caso) - SOLO si hay datos -->
                    <asp:Panel ID="pnlDocsCard" runat="server" Visible="false">
                        <div class="card ws-card mb-3">
                            <div class="card-body">
                                <div class="ws-title mb-2">
                                    <asp:Literal ID="litDocsTitle" runat="server" />
                                </div>

                                <asp:Panel ID="pnlDocs" runat="server" Visible="false">
                                    <asp:HiddenField ID="hfMotivoEliminarAdjunto" runat="server" />
                                    <div class="list-group">
                                        <asp:Repeater ID="rptDocs" runat="server" OnItemCommand="rptDocs_ItemCommand">
                                            <ItemTemplate>
                                                <div class="list-group-item d-flex justify-content-between align-items-center">
                                                    <div>
                                                        <div class="fw-semibold">
                                                            <%# !string.IsNullOrWhiteSpace(Convert.ToString(Eval("FileName")))
                                                                    ? Eval("FileName")
                                                                    : (!string.IsNullOrWhiteSpace(Convert.ToString(Eval("DocumentoId")))
                                                                        ? ("DocId: " + Eval("DocumentoId"))
                                                                        : "Documento") %>
                                                        </div>

                                                        <div class="text-muted small">
                                                            Tipo: <%# Eval("Tipo") %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("DocumentoId"))) ? "" : (" | docId: " + Eval("DocumentoId")) %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("CarpetaId"))) ? "" : (" | carpetaId: " + Eval("CarpetaId")) %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("FicheroId"))) ? "" : (" | ficheroId: " + Eval("FicheroId")) %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("TareaId"))) ? "" : (" | tareaId: " + Eval("TareaId")) %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("Fecha"))) ? "" : (" | fecha: " + Eval("Fecha")) %>
                                                            <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("Usuario"))) ? "" : (" | usuario: " + Eval("Usuario")) %>
                                                        </div>
                                                    </div>

                                                    <div class="d-flex gap-2">
                                                        <asp:HyperLink runat="server" CssClass="btn btn-sm btn-outline-primary"
                                                            NavigateUrl='<%# Eval("ViewerUrl") %>' Target="_blank"
                                                            Visible='<%# !string.IsNullOrWhiteSpace(Convert.ToString(Eval("ViewerUrl"))) %>'>
                                                            Ver
                                                        </asp:HyperLink>

                                                        <asp:LinkButton ID="lnkEliminarAdjuntoInst" runat="server"
                                                            CssClass="btn btn-sm btn-outline-danger"
                                                            CommandName="EliminarAdjuntoInst"
                                                            CommandArgument='<%# Eval("StoredFileName") + "|" + Eval("TareaId") + "|" + Eval("FileName") %>'
                                                            Visible='<%# Convert.ToBoolean(Eval("PuedeEliminar")) %>'
                                                            CausesValidation="false"
                                                            OnClientClick='<%# "return wfConfirmarEliminarAdjunto(\"" + hfMotivoEliminarAdjunto.ClientID + "\",\"" + System.Web.HttpUtility.JavaScriptStringEncode(Convert.ToString(Eval("FileName"))) + "\",\"" + System.Web.HttpUtility.JavaScriptStringEncode(Convert.ToString(Eval("TareaId"))) + "\");" %>'>
                                                            Eliminar
                                                        </asp:LinkButton>
                                                    </div>
                                                </div>
                                            </ItemTemplate>
                                        </asp:Repeater>
                                    </div>
                                </asp:Panel>

                                <!-- dejamos el pnlDocsEmpty pero el card completo se oculta cuando no hay docs -->
                                <asp:Panel ID="pnlDocsEmpty" runat="server" Visible="false" CssClass="ws-muted small">
                                    Seleccioná una instancia y presioná “Datos” para ver documentos del caso.
                                </asp:Panel>
                            </div>
                        </div>
                    </asp:Panel>

                    <!-- Auditoría documental - SOLO si hay datos -->
                    <asp:Panel ID="pnlDocAuditCard" runat="server" Visible="false">
                        <div class="card ws-card mb-3">
                            <div class="card-body">
                                <div class="ws-title mb-2">Auditoría documental</div>

                                <asp:Panel ID="pnlDocAudit" runat="server" Visible="false">
                                    <div class="d-flex align-items-center gap-2 mb-2">
                                        <asp:CheckBox ID="chkDocAuditDedup" runat="server"
                                            AutoPostBack="true"
                                            OnCheckedChanged="chkDocAuditDedup_CheckedChanged" />
                                        <span class="small text-muted">Mostrar deduplicado (último por documento/scope)</span>
                                    </div>

                                    <asp:GridView ID="gvDocAudit" runat="server"
                                        CssClass="table table-sm table-hover align-middle"
                                        AutoGenerateColumns="false"
                                        GridLines="None">
                                        <Columns>
                                            <asp:BoundField DataField="FechaAlta" HeaderText="Fecha" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" />
                                            <asp:BoundField DataField="Accion" HeaderText="Acción" />
                                            <asp:BoundField DataField="Scope" HeaderText="Scope" />
                                            <asp:BoundField DataField="NodoTipo" HeaderText="Nodo" />
                                            <asp:BoundField DataField="TareaId" HeaderText="Tarea" />
                                            <asp:BoundField DataField="Tipo" HeaderText="Tipo" />
                                            <asp:BoundField DataField="DocumentoId" HeaderText="DocId" />

                                            <asp:TemplateField HeaderText="Visor">
                                                <ItemTemplate>
                                                    <asp:HyperLink runat="server" CssClass="btn btn-sm btn-outline-primary"
                                                        NavigateUrl='<%# Eval("ViewerUrl") %>' Target="_blank"
                                                        Visible='<%# !string.IsNullOrWhiteSpace(Convert.ToString(Eval("ViewerUrl"))) %>'>
                                                        Ver
                                                    </asp:HyperLink>
                                                </ItemTemplate>
                                            </asp:TemplateField>
                                        </Columns>
                                    </asp:GridView>
                                </asp:Panel>

                                <!-- dejamos el empty pero el card completo se oculta cuando no hay filas -->
                                <asp:Panel ID="pnlDocAuditEmpty" runat="server" Visible="false" CssClass="ws-muted small">
                                    Seleccioná una instancia y presioná “Datos” o “Logs” para ver auditoría documental.
                                </asp:Panel>
                            </div>
                        </div>
                    </asp:Panel>

                    <!-- Logs -->
                    <asp:Panel ID="pnlLogsCard" runat="server" Visible="true">
                        <div class="card ws-card">
                            <div class="card-body">
                                <div class="d-flex align-items-center justify-content-between mb-2 gap-2">
                                    <div class="ws-title">Logs</div>
                                    <div class="ws-resize-hint text-end">Arrastrá la esquina inferior derecha para agrandar</div>
                                </div>

                                <asp:Panel ID="pnlLogs" runat="server" Visible="false">
                                    <pre class="log-view"><asp:Literal ID="litLogs" runat="server"></asp:Literal></pre>
                                </asp:Panel>

                                <asp:Panel ID="pnlLogsEmpty" runat="server" Visible="true" CssClass="ws-muted small">
                                    Seleccioná una instancia y presioná “Logs”.
                                </asp:Panel>
                            </div>
                        </div>
                    </asp:Panel>

                </div>
            </div>

        </div>
    </main>
     <script src="Scripts/bootstrap.bundle.min.js"></script>

    <script>
        function wfConfirmarEliminarAdjunto(hiddenId, fileName, tareaId) {
            var nombre = (fileName || '').trim();
            if (!nombre) nombre = '(sin nombre)';

            var msg = 'Vas a eliminar el adjunto:\n\n' + nombre;
            if ((tareaId || '').trim() !== '') {
                msg += '\nTarea origen: ' + tareaId;
            }
            msg += '\n\nEl archivo se quitará de la instancia actual y quedará auditado en Logs.';

            if (!confirm(msg)) return false;

            var m = prompt('Motivo de eliminación para "' + nombre + '":', '');
            if (m === null) return false;

            m = (m || '').trim();
            if (!m) {
                alert('Debe indicar un motivo.');
                return false;
            }

            var hf = document.getElementById(hiddenId);
            if (hf) hf.value = m;

            return true;
        }
    </script>


    <script>
        (function () {
            function norm(v) { return (v || '').toString().trim().toLowerCase(); }

            function getEls() {
                return {
                    txt: document.getElementById('<%= txtDef.ClientID %>'),
                    hid: document.getElementById('<%= hidDefId.ClientID %>'),
                    box: document.getElementById('defResults'),
                    empty: document.getElementById('defEmpty'),
                    clear: document.getElementById('btnDefClear'),
                    form: document.getElementById('<%= form1.ClientID %>')
                };
            }

            function items(box) {
                return box ? Array.prototype.slice.call(box.querySelectorAll('.ws-def-item')) : [];
            }

            function openBox(e) {
                if (e.box) e.box.classList.add('show');
                filterDefs(e);
            }

            function closeBox(e) {
                if (e.box) e.box.classList.remove('show');
            }

            function syncDefId(e) {
                if (!e.txt || !e.hid || !e.box) return;
                var val = norm(e.txt.value);
                var found = '';
                items(e.box).some(function (it) {
                    var display = norm(it.getAttribute('data-display'));
                    var code = norm(it.getAttribute('data-code'));
                    var name = norm(it.getAttribute('data-name'));
                    if (display === val || code === val || name === val) {
                        found = it.getAttribute('data-id') || '';
                        return true;
                    }
                    return false;
                });
                e.hid.value = found;
            }

            function filterDefs(e) {
                if (!e.txt || !e.box) return;
                var q = norm(e.txt.value);
                var visible = 0;
                items(e.box).forEach(function (it) {
                    var search = norm(it.getAttribute('data-search'));
                    var show = !q || search.indexOf(q) >= 0;
                    if (show && visible < 80) {
                        it.style.display = '';
                        visible++;
                    } else {
                        it.style.display = 'none';
                    }
                });
                if (e.empty) e.empty.classList.toggle('show', visible === 0);
            }

            function selectItem(e, it) {
                if (!e.txt || !e.hid || !it) return;
                e.txt.value = it.getAttribute('data-display') || '';
                e.hid.value = it.getAttribute('data-id') || '';
                closeBox(e);

                // Mantiene el comportamiento esperado del combo: al elegir una definición,
                // se actualiza el listado sin obligar al usuario a presionar Buscar/Refrescar.
                if (typeof window.__doPostBack === 'function' && e.txt.name) {
                    window.setTimeout(function () { window.__doPostBack(e.txt.name, ''); }, 0);
                }
            }

            function wireDefPicker() {
                var e = getEls();
                if (!e.txt || !e.box) return;

                e.txt.addEventListener('focus', function () { openBox(e); });
                e.txt.addEventListener('click', function () { openBox(e); });
                e.txt.addEventListener('input', function () {
                    if (e.hid) e.hid.value = '';
                    openBox(e);
                });
                e.txt.addEventListener('change', function () { syncDefId(e); });
                e.txt.addEventListener('blur', function () {
                    window.setTimeout(function () { syncDefId(e); closeBox(e); }, 180);
                });

                items(e.box).forEach(function (it) {
                    it.addEventListener('mousedown', function (ev) {
                        ev.preventDefault();
                        selectItem(e, it);
                    });
                });

                if (e.clear) {
                    e.clear.addEventListener('mousedown', function (ev) {
                        ev.preventDefault();
                        e.txt.value = '';
                        if (e.hid) e.hid.value = '';
                        openBox(e);
                        e.txt.focus();
                    });
                }

                if (e.form) e.form.addEventListener('submit', function () { syncDefId(e); });
            }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', wireDefPicker);
            else wireDefPicker();
        })();
    </script>

</form>
</body>
</html>
