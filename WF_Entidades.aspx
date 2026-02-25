<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Entidades.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Entidades" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Entidades</title>
    <meta charset="utf-8" />

    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <script src="Scripts/bootstrap.bundle.min.js"></script>

    <style>
        body { padding: 12px; background: #f6f7fb; }

        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(16,24,40,.06); border-radius: 16px; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table> :not(caption)>*>* { vertical-align: middle; }
        pre.ws-pre { max-height: 260px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 10px; font-size: .75rem; border-radius: 12px; }
        .ws-kv { font-size: .85rem; }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .ws-pill-muted { background: rgba(108,117,125,.10); color: #6c757d; border-color: rgba(108,117,125,.18); }
        .ws-pill-ok { background: rgba(25,135,84,.10); color: #198754; border-color: rgba(25,135,84,.20); }
        .ws-pill-err { background: rgba(220,53,69,.10); color: #dc3545; border-color: rgba(220,53,69,.20); }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="container-fluid">

            <div class="ws-topbar p-3 mb-3 ws-card">
                <div class="d-flex align-items-center justify-content-between">
                    <div>
                        <div class="ws-title">Entidades</div>
                        <div class="ws-muted small">Bandeja de casos orientados a proceso</div>
                    </div>

                    <div class="d-flex gap-2">
                        <asp:HyperLink ID="lnkInstancias" runat="server" NavigateUrl="~/WF_Instancias.aspx" CssClass="btn btn-sm btn-outline-secondary">
                            Ver instancias
                        </asp:HyperLink>
                    </div>
                </div>
            </div>

            <!-- Filtros -->
            <div class="ws-card card mb-3">
                <div class="card-body">
                    <div class="row g-2 align-items-end">
                        <div class="col-md-4">
                            <label class="form-label mb-0">TipoEntidad:</label>
                            <asp:DropDownList ID="ddlTipo" runat="server"
                                CssClass="form-select form-select-sm"
                                AutoPostBack="true"
                                OnSelectedIndexChanged="ddlTipo_SelectedIndexChanged" />
                        </div>

                        <div class="col-md-4">
                            <label class="form-label mb-0">Buscar:</label>
                            <div class="input-group input-group-sm">
                                <asp:TextBox ID="txtBuscar" runat="server" CssClass="form-control" placeholder="Ej: número, cuit, proveedor, empresa..." />
                                <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn btn-primary" OnClick="btnBuscar_Click" />
                                <asp:Button ID="btnLimpiar" runat="server" Text="Limpiar" CssClass="btn btn-outline-secondary" OnClick="btnLimpiar_Click" />
                            </div>
                            <div class="form-text ws-muted">Busca en índices (WF_EntidadIndice) y, si no hay, cae a DataJson.</div>
                        </div>

                        <div class="col-md-4">
                            <label class="form-label mb-0">Estado:</label>
                            <div class="btn-group btn-group-sm w-100" role="group">
                                <asp:LinkButton ID="lnkEstadoTodos" runat="server" CssClass="btn btn-outline-secondary" CommandArgument="" OnClick="lnkEstado_Click">Todos</asp:LinkButton>
                                <asp:LinkButton ID="lnkEstadoIniciado" runat="server" CssClass="btn btn-outline-primary" CommandArgument="Iniciado" OnClick="lnkEstado_Click">Iniciado</asp:LinkButton>
                                <asp:LinkButton ID="lnkEstadoFinalizado" runat="server" CssClass="btn btn-outline-success" CommandArgument="Finalizado" OnClick="lnkEstado_Click">Finalizado</asp:LinkButton>
                                <asp:LinkButton ID="lnkEstadoError" runat="server" CssClass="btn btn-outline-danger" CommandArgument="Error" OnClick="lnkEstado_Click">Error</asp:LinkButton>
                            </div>
                        </div>

                        <div class="row g-2 align-items-end mt-1">
                            <div class="col-md-4">
                                <label class="form-label mb-0">Índice (Key):</label>
                                <asp:DropDownList ID="ddlIdxKey" runat="server"
                                    CssClass="form-select form-select-sm"
                                    AutoPostBack="true"
                                    OnSelectedIndexChanged="ddlIdxKey_SelectedIndexChanged" />
                            </div>

                            <div class="col-md-4">
                                <label class="form-label mb-0">Valor (Value):</label>
                                <asp:TextBox ID="txtIdxValue" runat="server"
                                    CssClass="form-control form-control-sm"
                                    placeholder="Ej: 30-123..., 0001-00001234, proveedor..." />
                                <div class="form-text ws-muted">Filtra por WF_EntidadIndice (Key/ValueNorm).</div>
                            </div>

                            <div class="col-md-4 d-flex align-items-center gap-3">
                                <div class="form-check mt-4">
                                    <asp:CheckBox ID="chkSoloActivas" runat="server"
                                        CssClass="form-check-input"
                                        AutoPostBack="true"
                                        OnCheckedChanged="chkSoloActivas_CheckedChanged" />
                                    <label class="form-check-label ws-muted" for="<%= chkSoloActivas.ClientID %>">Solo activas</label>
                                </div>

                                <asp:Button ID="btnFiltrarIdx" runat="server" Text="Aplicar"
                                    CssClass="btn btn-sm btn-primary mt-4"
                                    OnClick="btnFiltrarIdx_Click" />
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- KPIs (según filtros) -->
            <div class="ws-card card mb-3">
                <div class="card-body py-3">
                    <div class="d-flex align-items-center justify-content-between">
                        <div>
                            <div class="fw-semibold">Resumen</div>
                            <div class="ws-muted small">Conteos por estado según filtros actuales (tipo/búsqueda/índice/solo activas).</div>
                        </div>
                        <div class="ws-muted small">
                            Mostrando: <asp:Label ID="lblKpiMostrando" runat="server" Text="0" />
                        </div>
                    </div>

                   <div class="row text-center mt-3 g-2">
                        <div class="col-6 col-md-3">
                            <div class="fw-bold fs-5">
                                <asp:LinkButton ID="kpiTodos" runat="server" CssClass="btn btn-link p-0 text-decoration-none"
                                    CommandArgument="" OnClick="lnkEstado_Click">
                                    <asp:Label ID="lblKpiTotal" runat="server" Text="0" />
                                </asp:LinkButton>
                            </div>
                            <div class="ws-muted small">Total</div>
                        </div>

                        <div class="col-6 col-md-3">
                            <div class="fw-bold">
                                <asp:LinkButton ID="kpiIniciado" runat="server" CssClass="btn btn-link p-0 text-decoration-none text-primary"
                                    CommandArgument="Iniciado" OnClick="lnkEstado_Click">
                                    <asp:Label ID="lblKpiIniciado" runat="server" Text="0" />
                                </asp:LinkButton>
                            </div>
                            <div class="ws-muted small">Iniciado</div>
                        </div>

                        <div class="col-6 col-md-3">
                            <div class="fw-bold">
                                <asp:LinkButton ID="kpiFinalizado" runat="server" CssClass="btn btn-link p-0 text-decoration-none text-success"
                                    CommandArgument="Finalizado" OnClick="lnkEstado_Click">
                                    <asp:Label ID="lblKpiFinalizado" runat="server" Text="0" />
                                </asp:LinkButton>
                            </div>
                            <div class="ws-muted small">Finalizado</div>
                        </div>

                        <div class="col-6 col-md-3">
                            <div class="fw-bold">
                                <asp:LinkButton ID="kpiError" runat="server" CssClass="btn btn-link p-0 text-decoration-none text-danger"
                                    CommandArgument="Error" OnClick="lnkEstado_Click">
                                    <asp:Label ID="lblKpiError" runat="server" Text="0" />
                                </asp:LinkButton>
                            </div>
                            <div class="ws-muted small">Error</div>
                        </div>
                    </div>


                </div>
            </div>
            <!-- Grilla -->
            <div class="ws-card card ws-grid mb-3">
                <div class="card-body p-0">
                    <asp:GridView ID="gvEnt" runat="server"
                        CssClass="table table-sm table-hover mb-0"
                        AutoGenerateColumns="False"
                        DataKeyNames="EntidadId"
                        AllowPaging="True"
                        PageSize="20"
                        OnPageIndexChanging="gvEnt_PageIndexChanging"
                        OnRowCommand="gvEnt_RowCommand">
                        <Columns>
                            <asp:BoundField DataField="EntidadId" HeaderText="Id" ItemStyle-Width="80px" />
                            <asp:BoundField DataField="TipoEntidad" HeaderText="Tipo" />
                            <asp:TemplateField HeaderText="Estado" ItemStyle-Width="140px">
                                <ItemTemplate>
                                    <span class='<%# Eval("EstadoActual").ToString() == "Finalizado" ? "ws-pill ws-pill-ok" :
                                                    (Eval("EstadoActual").ToString() == "Error" ? "ws-pill ws-pill-err" :
                                                    (Eval("EstadoActual").ToString() == "Iniciado" ? "ws-pill" : "ws-pill ws-pill-muted")) %>'>
                                        <%# Eval("EstadoActual") %>
                                    </span>
                                </ItemTemplate>
                            </asp:TemplateField>

                            <asp:BoundField DataField="Total" HeaderText="Total" DataFormatString="{0:N2}" ItemStyle-Width="120px" />
                            <asp:BoundField DataField="CreadoUtc" HeaderText="Creado" DataFormatString="{0:dd/MM/yyyy HH:mm}" ItemStyle-Width="160px" />
                            <asp:BoundField DataField="ActualizadoUtc" HeaderText="Actualizado" DataFormatString="{0:dd/MM/yyyy HH:mm}" ItemStyle-Width="160px" />
                            <asp:BoundField DataField="TareaPendiente" HeaderText="Tarea pendiente" />
                            <asp:BoundField DataField="UsuarioAsignado" HeaderText="Asignado a" ItemStyle-Width="140px" />
                            <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence"
                                DataFormatString="{0:dd/MM/yyyy}"
                                ItemStyle-Width="120px" />
                            <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="220px">
                                <ItemTemplate>
                                    <asp:LinkButton ID="lnkVer" runat="server" CssClass="btn btn-sm btn-outline-primary"
                                        CommandName="Sel" CommandArgument='<%# Eval("EntidadId") %>'>
                                        Ver
                                    </asp:LinkButton>

                                    <asp:HyperLink ID="lnkInst" runat="server" CssClass="btn btn-sm btn-outline-secondary ms-1"
                                        NavigateUrl='<%# Eval("InstanciaId") == DBNull.Value ? "" : ("~/WF_Instancias.aspx?inst=" + Eval("InstanciaId")) %>'
                                        Visible='<%# Eval("InstanciaId") != DBNull.Value %>'>
                                        Ir a instancia
                                    </asp:HyperLink>
                                </ItemTemplate>
                            </asp:TemplateField>
                        </Columns>
                    </asp:GridView>
                </div>
            </div>

            <!-- Detalle -->
            <asp:Panel ID="pnlDetalle" runat="server" Visible="false" CssClass="ws-card card">
                <div class="card-body">
                    <div class="d-flex align-items-center gap-2">
                        <asp:HyperLink ID="lnkVerInst" runat="server" CssClass="btn btn-sm btn-outline-secondary" Visible="false">
                            Ver instancia
                        </asp:HyperLink>

                        <asp:HyperLink ID="lnkVerLogs" runat="server" CssClass="btn btn-sm btn-outline-secondary" Visible="false">
                            Ver logs
                        </asp:HyperLink>

                        <div class="form-check ms-2">
                            <asp:CheckBox ID="chkModoTecnico" runat="server"
                                CssClass="form-check-input"
                                AutoPostBack="true"
                                OnCheckedChanged="chkModoTecnico_CheckedChanged" />
                            <label class="form-check-label ws-muted" for="<%= chkModoTecnico.ClientID %>">Modo técnico</label>
                        </div>
                    </div>
                    <div class="d-flex align-items-center justify-content-between mb-2">
                        <div>
                            <div class="ws-title">Detalle de entidad</div>
                            <div class="ws-muted small">
                                <asp:Literal ID="litDetalleSub" runat="server" />
                            </div>
                        </div>
                        <asp:LinkButton ID="btnCerrarDetalle" runat="server" CssClass="btn btn-sm btn-outline-secondary" OnClick="btnCerrarDetalle_Click">
                            Cerrar
                        </asp:LinkButton>
                    </div>

                   <!-- Resumen funcional (siempre visible) -->
                    <asp:Panel ID="pnlFuncional" runat="server">
                        <div class="row g-3 mb-2">
                            <div class="col-md-3">
                                <div class="ws-muted small">Estado</div>
                                <div class="fw-semibold"><asp:Literal ID="litEstado" runat="server" /></div>
                            </div>
                            <div class="col-md-3">
                                <div class="ws-muted small">Tipo</div>
                                <div class="fw-semibold"><asp:Literal ID="litTipo" runat="server" /></div>
                            </div>
                            <div class="col-md-3">
                                <div class="ws-muted small">Total</div>
                                <div class="fw-semibold"><asp:Literal ID="litTotal" runat="server" /></div>
                            </div>
                            <div class="col-md-3">
                                <div class="ws-muted small">Instancia</div>
                                <div class="fw-semibold"><asp:Literal ID="litInst" runat="server" /></div>
                            </div>
                        </div>

                        <hr class="my-3" />
                    </asp:Panel>

                    <!-- Técnico (solo si Modo técnico = ON) -->
                    <asp:Panel ID="pnlTecnico" runat="server" Visible="false">
                        <div class="row g-3">
                            <div class="col-md-6">
                                <div class="ws-muted small mb-1">Snapshot (DataJson)</div>
                                <div class="d-flex justify-content-between align-items-center mb-1">
                                    <div class="ws-muted small">Snapshot (DataJson)</div>

                                    <button type="button" class="btn btn-sm btn-outline-secondary" onclick="wsCopyJson()">
                                        Copiar JSON
                                    </button>
                                </div>

                                <asp:HiddenField ID="hfJsonRaw" runat="server" />
                                <pre class="ws-pre"><asp:Literal ID="litJson" runat="server" /></pre>
                            </div>

                            <div class="col-md-6">
                                <div class="ws-muted small mb-1">Índices</div>
                                <asp:GridView ID="gvIdx" runat="server" AutoGenerateColumns="False"
                                    CssClass="table table-sm table-striped ws-grid mb-0">
                                    <Columns>
                                        <asp:BoundField DataField="Key" HeaderText="Key" ItemStyle-Width="160px" />
                                        <asp:BoundField DataField="Value" HeaderText="Value" />
                                        <asp:BoundField DataField="SourcePath" HeaderText="Path" ItemStyle-Width="200px" />
                                    </Columns>
                                </asp:GridView>

                                <div class="ws-muted small mt-3 mb-1">Items</div>
                                <asp:GridView ID="gvItems" runat="server" AutoGenerateColumns="False"
                                    CssClass="table table-sm table-striped ws-grid mb-0">
                                    <Columns>
                                        <asp:BoundField DataField="ItemIndex" HeaderText="#" ItemStyle-Width="60px" />
                                        <asp:BoundField DataField="Descripcion" HeaderText="Descripción" />
                                        <asp:BoundField DataField="Cantidad" HeaderText="Cant." DataFormatString="{0:N4}" ItemStyle-Width="100px" />
                                        <asp:BoundField DataField="Importe" HeaderText="Importe" DataFormatString="{0:N2}" ItemStyle-Width="120px" />
                                    </Columns>
                                </asp:GridView>
                            </div>
                        </div>
                    </asp:Panel>
                </div>
            </asp:Panel>

        </div>
    </main>
</form>

    <script>
        function wsCopyJson() {
            try {
                var hf = document.getElementById('<%= hfJsonRaw.ClientID %>');
                var txt = hf ? (hf.value || '') : '';
                if (!txt) return;

                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(txt);
                } else {
                    var ta = document.createElement('textarea');
                    ta.value = txt;
                    ta.style.position = 'fixed';
                    ta.style.left = '-9999px';
                    document.body.appendChild(ta);
                    ta.select();
                    document.execCommand('copy');
                    document.body.removeChild(ta);
                }
            } catch (e) { }
        }
    </script>
</body>
</html>
