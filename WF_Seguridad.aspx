<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Seguridad.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Seguridad" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html>
<head runat="server">
    <title>WF - Seguridad</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <link href="Content/Site.css" rel="stylesheet" />

    <style>
        body { background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table> :not(caption)>*>* { vertical-align: middle; }
        .grid-small td, .grid-small th { padding: 6px 8px; font-size: .82rem; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .form-floating.wf-float-sm > .form-control {
          height: calc(2.55rem + 2px);
          min-height: calc(2.55rem + 2px);
          padding: 1.15rem .75rem .35rem;
          font-size: .95rem;
        }

        .form-floating.wf-float-sm > label {
          padding: .55rem .75rem;
          font-size: .85rem;
        }

        /* Opcional: que el autofill no te deforme el alto */
        .form-floating.wf-float-sm > .form-control:-webkit-autofill {
          padding-top: 1.15rem;
        }  
        #panel-asig .card.border-primary { border-width: 2px !important; }
    </style> 

</head>
<body>
<form id="form1" runat="server">
    <asp:HiddenField runat="server" ID="hfActiveTab" />
    <asp:HiddenField runat="server" ID="hfActiveAsigBlock" />
    <asp:HiddenField runat="server" ID="hfScrollY" />


     <!-- Topbar coherente -->
 <ws:Topbar runat="server" ID="Topbar1" />

 <main class="container-fluid px-3 px-md-4 py-4">
    <div class="container-fluid">
        <div class="mt-3 mb-2">
            <h3 class="mb-0">Seguridad (Usuarios / Roles / Permisos)</h3>
            <div class="text-muted">Solo ADMIN o SEGURIDAD_ABM</div>
        </div>

        <asp:Label runat="server" ID="lblMsg" Visible="false" />

        <ul class="nav nav-tabs" id="tabsSeg" role="tablist">
            <li class="nav-item" role="presentation">
                <button class="nav-link active" id="tab-usuarios" data-bs-toggle="tab" data-bs-target="#panel-usuarios" type="button" role="tab">
                    Usuarios
                </button>
            </li>
            <li class="nav-item" role="presentation">
                <button class="nav-link" id="tab-roles" data-bs-toggle="tab" data-bs-target="#panel-roles" type="button" role="tab">
                    Roles
                </button>
            </li>
            <li class="nav-item" role="presentation">
                <button class="nav-link" id="tab-permisos" data-bs-toggle="tab" data-bs-target="#panel-permisos" type="button" role="tab">
                    Permisos
                </button>
            </li>
            <li class="nav-item" role="presentation">
                <button class="nav-link" id="tab-asig" data-bs-toggle="tab" data-bs-target="#panel-asig" type="button" role="tab">
                    Asignaciones
                </button>
            </li>
        </ul>

        <div class="tab-content border border-top-0 p-3 bg-white" id="tabsSegContent">

            <!-- ===== USUARIOS ===== -->
            <div class="tab-pane fade show active" id="panel-usuarios" role="tabpanel">
               <div class="row g-2 align-items-end mb-3">


                      <div class="col-12 col-md-3">
                        <div class="form-floating wf-float-sm">
                          <asp:TextBox runat="server" ID="txtUserKey" CssClass="form-control"
                            placeholder="OMARD\USUARIO1" />
                          <label for="<%= txtUserKey.ClientID %>">UserKey</label>
                        </div>
                      </div>

                      <div class="col-12 col-md-3">
                        <div class="form-floating wf-float-sm">
                          <asp:TextBox runat="server" ID="txtDisplayName" CssClass="form-control"
                            placeholder="Nombre a mostrar" />
                          <label for="<%= txtDisplayName.ClientID %>">DisplayName</label>
                        </div>
                      </div>

                      <div class="col-auto">
                        <asp:Button runat="server" ID="btnUserAdd" Text="Agregar / Actualizar"
                          CssClass="btn btn-primary btn-sm" OnClick="btnUserAdd_Click" />
                      </div>

                      <div class="col-auto">
                        <asp:Button runat="server" ID="btnUserClear" Text="Limpiar"
                          CssClass="btn btn-outline-secondary btn-sm" OnClick="btnUserClear_Click" />
                      </div>

                    </div>


                <asp:GridView runat="server" ID="gvUsers" CssClass="table table-sm table-hover"
                    AutoGenerateColumns="false" DataKeyNames="UserKey" OnRowCommand="gvUsers_RowCommand">
                    <Columns>
                        <asp:BoundField DataField="UserKey" HeaderText="UserKey" />
                        <asp:BoundField DataField="DisplayName" HeaderText="Nombre" />
                        <asp:BoundField DataField="Activo" HeaderText="Activo" />
                        <asp:BoundField DataField="FechaAlta" HeaderText="Alta" DataFormatString="{0:yyyy-MM-dd HH:mm}" />
                        <asp:TemplateField HeaderText="">
                            <ItemTemplate>
                                <asp:LinkButton runat="server" CommandName="EditUser" CommandArgument='<%# Eval("UserKey") %>' CssClass="btn btn-sm btn-outline-primary">Editar</asp:LinkButton>
                                <asp:LinkButton runat="server" CommandName="ToggleUser" CommandArgument='<%# Eval("UserKey") %>' CssClass="btn btn-sm btn-outline-warning ms-1">Activar/Desactivar</asp:LinkButton>
                            </ItemTemplate>
                        </asp:TemplateField>
                    </Columns>
                </asp:GridView>
            </div>

            <!-- ===== ROLES ===== -->
            <div class="tab-pane fade" id="panel-roles" role="tabpanel">
                   <div class="row g-2 align-items-end mb-3">

                    <div class="col-12 col-md-3">
                        <div class="form-floating wf-float-sm">
                            <asp:TextBox runat="server" ID="txtRolKey" CssClass="form-control"
                                placeholder="IT / COMPRAS / DIR_GENERAL" />
                            <label for="<%= txtRolKey.ClientID %>">RolKey</label>
                        </div>
                    </div>

                    <div class="col-12 col-md-5">
                        <div class="form-floating wf-float-sm">
                            <asp:TextBox runat="server" ID="txtRolNombre" CssClass="form-control"
                                placeholder="Nombre del rol" />
                            <label for="<%= txtRolNombre.ClientID %>">Nombre</label>
                        </div>
                    </div>
                    <div class="col-auto">
                        <asp:Button runat="server" ID="btnRolAdd" Text="Agregar / Actualizar"
                            CssClass="btn btn-primary btn-sm" OnClick="btnRolAdd_Click" />
                    </div>

                    <div class="col-auto">
                        <asp:Button runat="server" ID="btnRolClear" Text="Limpiar"
                            CssClass="btn btn-outline-secondary btn-sm" OnClick="btnRolClear_Click" />
                    </div>

                <asp:GridView runat="server" ID="gvRoles" CssClass="table table-sm table-hover"
                    AutoGenerateColumns="false" DataKeyNames="RolKey" OnRowCommand="gvRoles_RowCommand">
                    <Columns>
                        <asp:BoundField DataField="RolKey" HeaderText="RolKey" />
                        <asp:BoundField DataField="Nombre" HeaderText="Nombre" />
                        <asp:BoundField DataField="Activo" HeaderText="Activo" />
                        <asp:TemplateField HeaderText="">
                            <ItemTemplate>
                                <asp:LinkButton runat="server" CommandName="EditRol" CommandArgument='<%# Eval("RolKey") %>' CssClass="btn btn-sm btn-outline-primary">Editar</asp:LinkButton>
                                <asp:LinkButton runat="server" CommandName="ToggleRol" CommandArgument='<%# Eval("RolKey") %>' CssClass="btn btn-sm btn-outline-warning ms-1">Activar/Desactivar</asp:LinkButton>
                            </ItemTemplate>
                        </asp:TemplateField>
                    </Columns>
                </asp:GridView>
            </div>
        </div>
            <!-- ===== PERMISOS ===== -->
            <div class="tab-pane fade" id="panel-permisos" role="tabpanel">
                   <div class="row g-2 align-items-end mb-3">

                        <div class="col-12 col-md-3">
                            <div class="form-floating wf-float-sm">
                                <asp:TextBox runat="server" ID="txtPermKey" CssClass="form-control"
                                    placeholder="WF_ADMIN / SEGURIDAD_ABM" />
                                <label for="<%= txtPermKey.ClientID %>">PermisoKey</label>
                            </div>
                        </div>

                        <div class="col-12 col-md-3">
                            <div class="form-floating wf-float-sm">
                                <asp:TextBox runat="server" ID="txtPermNombre" CssClass="form-control"
                                    placeholder="Nombre visible" />
                                <label for="<%= txtPermNombre.ClientID %>">Nombre</label>
                            </div>
                        </div>

                        <div class="col-12 col-md-4">
                            <div class="form-floating wf-float-sm">
                                <asp:TextBox runat="server" ID="txtPermDesc" CssClass="form-control"
                                    placeholder="Descripción" />
                                <label for="<%= txtPermDesc.ClientID %>">Descripción</label>
                            </div>
                        </div>

                        <div class="col-auto">
                            <asp:Button runat="server" ID="btnPermAdd" Text="Agregar / Actualizar"
                                CssClass="btn btn-primary btn-sm" OnClick="btnPermAdd_Click" />
                        </div>

                        <div class="col-auto">
                            <asp:Button runat="server" ID="btnPermClear" Text="Limpiar"
                                CssClass="btn btn-outline-secondary btn-sm" OnClick="btnPermClear_Click" />
                        </div>

                    </div>

                <asp:GridView runat="server" ID="gvPermisos" CssClass="table table-sm table-hover"
                    AutoGenerateColumns="false" DataKeyNames="PermisoKey" OnRowCommand="gvPermisos_RowCommand">
                    <Columns>
                        <asp:BoundField DataField="PermisoKey" HeaderText="PermisoKey" />
                        <asp:BoundField DataField="Nombre" HeaderText="Nombre" />
                        <asp:BoundField DataField="Descripcion" HeaderText="Descripción" />
                        <asp:BoundField DataField="Activo" HeaderText="Activo" />
                        <asp:TemplateField HeaderText="">
                            <ItemTemplate>
                                <asp:LinkButton runat="server" CommandName="EditPerm" CommandArgument='<%# Eval("PermisoKey") %>' CssClass="btn btn-sm btn-outline-primary">Editar</asp:LinkButton>
                                <asp:LinkButton runat="server" CommandName="TogglePerm" CommandArgument='<%# Eval("PermisoKey") %>' CssClass="btn btn-sm btn-outline-warning ms-1">Activar/Desactivar</asp:LinkButton>
                            </ItemTemplate>
                        </asp:TemplateField>
                    </Columns>
                </asp:GridView>
            </div>

            <!-- ===== ASIGNACIONES ===== -->
            <div class="tab-pane fade" id="panel-asig" role="tabpanel">

                <div class="row g-3">

                    <!-- Usuario -> Roles -->
                    <div class="col-12 col-lg-4">
                        <div class="card" data-asigblock="user-roles">
                            <div class="card-header">Usuario → Roles</div>
                            <div class="card-body">
                                <label class="form-label">Usuario</label>
                                <asp:DropDownList runat="server" ID="ddlUserRoles" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="ddlUserRoles_SelectedIndexChanged" />
                                <div class="mt-2">
                                    <asp:CheckBoxList runat="server" ID="cblRoles" CssClass="form-check" RepeatLayout="Flow" />
                                </div>
                                <div class="mt-3 d-flex gap-2">
                                    <asp:Button runat="server" ID="btnSaveUserRoles" Text="Guardar" CssClass="btn btn-primary" OnClick="btnSaveUserRoles_Click" />
                                    <asp:Button runat="server" ID="btnReloadUserRoles" Text="Recargar" CssClass="btn btn-outline-secondary" OnClick="btnReloadUserRoles_Click" />
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Rol -> Permisos -->
                    <div class="col-12 col-lg-4">
                        <div class="card" data-asigblock="rol-perms">
                            <div class="card-header">Rol → Permisos</div>
                            <div class="card-body">
                                <label class="form-label">Rol</label>
                                <asp:DropDownList runat="server" ID="ddlRolPerms" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="ddlRolPerms_SelectedIndexChanged" />
                                <div class="mt-2">
                                    <asp:CheckBoxList runat="server" ID="cblPermsPorRol" CssClass="form-check" RepeatLayout="Flow" />
                                </div>
                                <div class="mt-3 d-flex gap-2">
                                    <asp:Button runat="server" ID="btnSaveRolPerms" Text="Guardar" CssClass="btn btn-primary" OnClick="btnSaveRolPerms_Click" />
                                    <asp:Button runat="server" ID="btnReloadRolPerms" Text="Recargar" CssClass="btn btn-outline-secondary" OnClick="btnReloadRolPerms_Click" />
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Usuario -> Permisos override -->
                    <div class="col-12 col-lg-4">
                        <div class="card" data-asigblock="user-perms">
                            <div class="card-header">Usuario → Permisos (Override)</div>
                            <div class="card-body">
                                <label class="form-label">Usuario</label>
                                <asp:DropDownList runat="server" ID="ddlUserPerms" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="ddlUserPerms_SelectedIndexChanged" />
                                <div class="mt-2">
                                    <asp:CheckBoxList runat="server" ID="cblPermsPorUser" CssClass="form-check" RepeatLayout="Flow" />
                                </div>
                                <div class="mt-3 d-flex gap-2">
                                    <asp:Button runat="server" ID="btnSaveUserPerms" Text="Guardar" CssClass="btn btn-primary" OnClick="btnSaveUserPerms_Click" />
                                    <asp:Button runat="server" ID="btnReloadUserPerms" Text="Recargar" CssClass="btn btn-outline-secondary" OnClick="btnReloadUserPerms_Click" />
                                </div>
                                <div class="form-text mt-2">Override sirve para ADMIN / excepciones puntuales.</div>
                            </div>
                        </div>
                    </div>

                </div>

            </div>

        </div>
    </div>
</main>
    <script src="Scripts/bootstrap.bundle.min.js"></script>

    <script>
        (function () {

            function setVal(id, v) {
                var el = document.getElementById(id);
                if (el) el.value = v || "";
            }
            function getVal(id) {
                var el = document.getElementById(id);
                return el ? (el.value || "") : "";
            }

            document.addEventListener("DOMContentLoaded", function () {

                var hfTab = "<%= hfActiveTab.ClientID %>";
      var hfBlock = "<%= hfActiveAsigBlock.ClientID %>";
      var hfScroll = "<%= hfScrollY.ClientID %>";

    // 1) Guardar tab activo cuando cambia
    var tabBtns = document.querySelectorAll('#tabsSeg button[data-bs-toggle="tab"]');
    tabBtns.forEach(function (btn) {
      btn.addEventListener("shown.bs.tab", function (e) {
        setVal(hfTab, e.target.getAttribute("data-bs-target"));
      });
      // Por si hay click antes del shown
      btn.addEventListener("click", function () {
        setVal(hfTab, btn.getAttribute("data-bs-target"));
      });
    });

    // 2) Guardar bloque activo dentro de Asignaciones (cuando hacés foco/click en un card)
    function markActiveCard(card) {
      document.querySelectorAll('#panel-asig .card').forEach(function (c) {
        c.classList.remove("border-primary");
        c.classList.remove("shadow-sm");
      });
      if (card) {
        card.classList.add("border-primary");
        card.classList.add("shadow-sm");
      }
    }

    document.querySelectorAll('#panel-asig .card[data-asigblock]').forEach(function (card) {
      card.addEventListener("mousedown", function () {
        setVal(hfTab, "#panel-asig");
        setVal(hfBlock, card.getAttribute("data-asigblock"));
      });
      card.addEventListener("focusin", function () {
        setVal(hfTab, "#panel-asig");
        setVal(hfBlock, card.getAttribute("data-asigblock"));
      });
    });

    // 3) Guardar scroll (antes de postback / navegación)
    window.addEventListener("beforeunload", function () {
      setVal(hfScroll, String(window.scrollY || 0));
    });

    // 4) Restaurar tab activo después del postback
    var activeTab = getVal(hfTab);
    if (activeTab) {
      var trigger = document.querySelector('#tabsSeg button[data-bs-target="' + activeTab + '"]');
      if (trigger && window.bootstrap && bootstrap.Tab) {
        new bootstrap.Tab(trigger).show();
      }
    }

    // 5) Restaurar bloque activo dentro de Asignaciones + foco
    var block = getVal(hfBlock);
    if (block) {
      var card = document.querySelector('#panel-asig .card[data-asigblock="' + block + '"]');
      if (card) {
        markActiveCard(card);

        // enfocar el primer input/select del bloque (queda súper pro)
        var first = card.querySelector("select, input, button, textarea, a");
        if (first) {
          setTimeout(function () { try { first.focus(); } catch(e) {} }, 50);
        }
      }
    }

    // 6) Restaurar scroll
    var y = parseInt(getVal(hfScroll) || "0", 10);
    if (!isNaN(y) && y > 0) {
      setTimeout(function () { window.scrollTo(0, y); }, 50);
    }

    // EXTRA: cuando cambian los combos de Asignaciones, fijar tab y bloque ANTES del postback
    var ddlUserRoles = document.getElementById("<%= ddlUserRoles.ClientID %>");
    var ddlRolPerms  = document.getElementById("<%= ddlRolPerms.ClientID %>");
    var ddlUserPerms = document.getElementById("<%= ddlUserPerms.ClientID %>");

      if (ddlUserRoles) ddlUserRoles.addEventListener("change", function () {
          setVal(hfTab, "#panel-asig");
          setVal(hfBlock, "user-roles");
      });
      if (ddlRolPerms) ddlRolPerms.addEventListener("change", function () {
          setVal(hfTab, "#panel-asig");
          setVal(hfBlock, "rol-perms");
      });
      if (ddlUserPerms) ddlUserPerms.addEventListener("change", function () {
          setVal(hfTab, "#panel-asig");
          setVal(hfBlock, "user-perms");
      });

  });

        })();
    </script>
</form>
</body>
</html>