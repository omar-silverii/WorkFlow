<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="WsTopbar.ascx.cs"
    Inherits="Intranet.WorkflowStudio.WebForms.Controls.WsTopbar" %>

<nav class="navbar navbar-expand-lg ws-topbar sticky-top">
    <div class="container-fluid px-3 px-md-4">

        <!-- Brand -->
        <asp:HyperLink ID="lnkBrand" runat="server" CssClass="navbar-brand fw-bold" NavigateUrl="~/Default.aspx">
            Workflow Studio <span class="ws-pill ms-2">Intranet</span>
        </asp:HyperLink>

        <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#wsNav"
                aria-controls="wsNav" aria-expanded="false" aria-label="Toggle navigation">
            <span class="navbar-toggler-icon"></span>
        </button>

        <div class="collapse navbar-collapse" id="wsNav">
            <ul class="navbar-nav ms-auto gap-lg-2">

                <li class="nav-item">
                    <asp:HyperLink ID="lnkInicio" runat="server" CssClass="nav-link" NavigateUrl="~/Default.aspx">Inicio</asp:HyperLink>
                </li>

                <li class="nav-item dropdown">
                    <a id="lnkWorkflows" runat="server" class="nav-link dropdown-toggle" href="#"
                       role="button" data-bs-toggle="dropdown" aria-expanded="false">
                        Workflows
                    </a>
                    <ul class="dropdown-menu">
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WorkflowUI.aspx") %>">➕ Nuevo / Editor</a></li>
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_Definiciones.aspx") %>">📋 Definiciones</a></li>
                    </ul>
                </li>

                <li class="nav-item dropdown">
                    <a id="lnkDocumentos" runat="server" class="nav-link dropdown-toggle" href="#"
                       role="button" data-bs-toggle="dropdown" aria-expanded="false">
                        Documentos
                    </a>
                    <ul class="dropdown-menu">
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_DocTipo.aspx") %>">📁 Tipos de documento</a></li>
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_DocTipoReglas.aspx") %>">🧠 Reglas de extracción</a></li>
                    </ul>
                </li>

                <li class="nav-item dropdown">
                    <a id="lnkTareas" runat="server" class="nav-link dropdown-toggle" href="#"
                       role="button" data-bs-toggle="dropdown" aria-expanded="false">
                        Tareas
                    </a>
                    <ul class="dropdown-menu">
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_Tareas.aspx") %>">🧑‍💻 Mis tareas</a></li>
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_Gerente_Tareas.aspx") %>">🧑‍💼 Gerencia</a></li>
                        <li><hr class="dropdown-divider" /></li>
                        <li><a class="dropdown-item" href="<%= ResolveUrl("~/WF_Instancias.aspx") %>">▶ Ejecuciones (Instancias)</a></li>
                    </ul>
                </li>

                <li class="nav-item">
                    <asp:HyperLink ID="lnkAdmin" runat="server" CssClass="nav-link" NavigateUrl="~/WF_Definiciones.aspx">Administración</asp:HyperLink>
                </li>

            </ul>
        </div>
    </div>
</nav>
