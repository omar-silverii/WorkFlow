<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_DocTipo.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_DocTipo" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>WF - DocTipos (ABM)</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />

    <!-- Bootstrap 5 (local) -->
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <script src="Scripts/bootstrap.bundle.min.js"></script>
     
       <style>
     body { padding: 12px; }
     .grid-small td, .grid-small th { padding: 4px 6px; font-size: .82rem; }
     pre.json-view { max-height: 280px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }
 </style>

    <!-- (Opcional) tu validador global, si ya lo tenés -->
    <script src="Scripts/inspectors/json.validator.js"></script>
</head>

<body>
<form id="form1" runat="server">
    <asp:ScriptManager runat="server" ID="sm" />

    <div class="page-wrap">
        <div class="d-flex align-items-center justify-content-between mb-3">
            <div>
                <h4 class="mb-3">DocTipos (Catálogo de documentos)</h4>
                <div class="muted">ABM de <b>WF_DocTipo</b>. Reglas Extract van por página aparte (WF_DocTipoReglaExtract).</div>
            </div>

            <div class="d-flex gap-2">
                <a class="btn btn-outline-light" href="Default.aspx">Volver</a>
                <button type="button" class="btn btn-primary" onclick="wfDocTipoOpenNew()">+ Nuevo DocTipo</button>
            </div>
        </div>

        <div class="card p-3 mb-3">
            <div class="row g-2 align-items-end">
                <div class="col-md-6">
                    <label class="form-label">Buscar</label>
                    <asp:TextBox runat="server" ID="txtQ" CssClass="form-control" placeholder="Código o nombre..." />
                </div>
                <div class="col-md-3">
                    <label class="form-label">Estado</label>
                    <asp:DropDownList runat="server" ID="ddlEstado" CssClass="form-select">
                        <asp:ListItem Text="Todos" Value="" />
                        <asp:ListItem Text="Activos" Value="1" />
                        <asp:ListItem Text="Inactivos" Value="0" />
                    </asp:DropDownList>
                </div>
                <div class="col-md-3 d-flex gap-2">
                    <asp:Button runat="server" ID="btnBuscar" Text="Buscar" CssClass="btn btn-outline-light w-100"
                        OnClick="btnBuscar_Click" />
                    <asp:Button runat="server" ID="btnLimpiar" Text="Limpiar" CssClass="btn btn-outline-secondary w-100"
                        OnClick="btnLimpiar_Click" />
                </div>
            </div>
        </div>

        <div class="card p-0">
            <div class="table-responsive">
                <asp:GridView runat="server" ID="gv" CssClass="table table-hover mb-0"
                    AutoGenerateColumns="False" DataKeyNames="DocTipoId"
                    OnRowCommand="gv_RowCommand" OnRowDataBound="gv_RowDataBound">
                    <Columns>
                        <asp:BoundField DataField="DocTipoId" HeaderText="Id" />
                        <asp:BoundField DataField="Codigo" HeaderText="Código" />
                        <asp:BoundField DataField="Nombre" HeaderText="Nombre" />
                        <asp:BoundField DataField="ContextPrefix" HeaderText="Prefix" />
                        <asp:TemplateField HeaderText="Estado">
                            <ItemTemplate>
                                <span class="badge badge-soft">
                                    <%# (Convert.ToBoolean(Eval("EsActivo")) ? "Activo" : "Inactivo") %>
                                </span>
                            </ItemTemplate>
                        </asp:TemplateField>

                        <asp:TemplateField HeaderText="Acciones">
                            <ItemTemplate>
                                <div class="d-flex gap-2">
                                    <asp:LinkButton runat="server" CssClass="btn btn-outline-light btn-sm btn-icon"
                                        CommandName="EDIT" CommandArgument='<%# Eval("DocTipoId") %>' ToolTip="Editar">
                                        ✎
                                    </asp:LinkButton>

                                    <asp:LinkButton runat="server" CssClass="btn btn-outline-warning btn-sm btn-icon"
                                        CommandName="TOGGLE" CommandArgument='<%# Eval("DocTipoId") %>' ToolTip="Activar/Desactivar">
                                        ⟳
                                    </asp:LinkButton>

                                    <asp:LinkButton runat="server" CssClass="btn btn-outline-danger btn-sm btn-icon"
                                        CommandName="DEL" CommandArgument='<%# Eval("DocTipoId") %>' ToolTip="Eliminar"
                                        OnClientClick="return confirm('¿Eliminar este DocTipo? (No afecta instancias, solo catálogo)');">
                                        🗑
                                    </asp:LinkButton>
                                </div>
                            </ItemTemplate>
                        </asp:TemplateField>
                    </Columns>
                    <EmptyDataTemplate>
                        <div class="p-3 muted">No hay DocTipos con ese filtro.</div>
                    </EmptyDataTemplate>
                </asp:GridView>
            </div>
        </div>

        <asp:Literal runat="server" ID="litMsg" />
    </div>

    <!-- MODAL: Alta / Edición -->
    <div class="modal fade" id="mdlDocTipo" tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-lg modal-dialog-scrollable">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title" id="mdlTitle">DocTipo</h5>
                    <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
                </div>

                <div class="modal-body">
                    <asp:HiddenField runat="server" ID="hfId" />

                    <div class="row g-3">
                        <div class="col-md-4">
                            <label class="form-label">Código</label>
                            <asp:TextBox runat="server" ID="txtCodigo" CssClass="form-control" placeholder="ORDEN_COMPRA" />
                            <div class="hint">Único. Usado por nodos y API.</div>
                        </div>

                        <div class="col-md-5">
                            <label class="form-label">Nombre</label>
                            <asp:TextBox runat="server" ID="txtNombre" CssClass="form-control" placeholder="Orden de Compra" />
                        </div>

                        <div class="col-md-3">
                            <label class="form-label">ContextPrefix</label>
                            <asp:TextBox runat="server" ID="txtPrefix" CssClass="form-control" placeholder="oc" />
                            <div class="hint">Ej: oc / np / fact</div>
                        </div>

                        <div class="col-md-6">
                            <label class="form-label">PlantillaPath (opcional)</label>
                            <asp:TextBox runat="server" ID="txtPlantilla" CssClass="form-control" placeholder="C:\plantillas\oc.docx" />
                        </div>

                        <div class="col-md-6">
                            <label class="form-label">RutaBase (opcional)</label>
                            <asp:TextBox runat="server" ID="txtRutaBase" CssClass="form-control" placeholder="C:\docs\compras\" />
                        </div>

                        <div class="col-md-3">
                            <label class="form-label">Activo</label>
                            <asp:CheckBox runat="server" ID="chkActivo" CssClass="form-check-input" />
                        </div>

                        <div class="col-12">
                            <div class="d-flex justify-content-between align-items-end">
                                <div>
                                    <label class="form-label mb-1">RulesJson override (opcional)</label>
                                    <div class="hint">
                                        Si está vacío, las reglas se construyen desde <b>WF_DocTipoReglaExtract</b>.
                                        Si lo llenás, este JSON “gana” (override).
                                    </div>
                                </div>
                                <div class="d-flex gap-2">
                                    <button type="button" class="btn btn-outline-light btn-sm" id="btnFmtJson">Formatear JSON</button>
                                </div>
                            </div>

                            <asp:TextBox runat="server" ID="txtRulesJson" TextMode="MultiLine" CssClass="form-control mono" Rows="10" />
                        </div>
                    </div>
                </div>

                <div class="modal-footer">
                    <asp:Button runat="server" ID="btnGuardar" Text="Guardar" CssClass="btn btn-primary"
                        OnClick="btnGuardar_Click" />
                    <button type="button" class="btn btn-outline-light" data-bs-dismiss="modal">Cerrar</button>
                </div>
            </div>
        </div>
    </div>

    <script>
        function wfDocTipoShowModal() {
            var el = document.getElementById('mdlDocTipo');
            var m = bootstrap.Modal.getOrCreateInstance(el);
            m.show();
        }
        function wfDocTipoOpenNew() {
            // Limpieza mínima en cliente (el server también limpia)
            document.getElementById('<%= hfId.ClientID %>').value = '';
            wfDocTipoShowModal();
        }

        // Validación + Formateo (usa tu módulo si existe)
        (function () {
            var ta = document.getElementById('<%= txtRulesJson.ClientID %>');
            var b = document.getElementById('btnFmtJson');
            if (!ta || !b) return;

            // Validator global (si lo tenés)
            if (window.WF_Json && typeof WF_Json.attachValidator === 'function') {
                WF_Json.attachValidator(ta);
            }

            // Formatter global (si lo tenés)
            if (window.WF_Json && typeof WF_Json.attachFormatterButton === 'function') {
                WF_Json.attachFormatterButton(b, ta);
                return;
            }

            // Fallback simple por si todavía no está attachFormatterButton
            b.addEventListener('click', function () {
                try {
                    var obj = JSON.parse(ta.value || '');
                    ta.value = JSON.stringify(obj, null, 2);
                } catch (e) { /* no-op */ }
            });
        })();
    </script>
</form>
</body>
</html>
