<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WorkflowUI.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WorkflowUI" %>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Workflow UI</title>
  <meta charset="utf-8" />
    <link rel="stylesheet" href="Content/bootstrap.min.css" />

   <link rel="stylesheet" href="Styles/workflow.ui.css" />
</head>
<body>
  <form id="form1" runat="server" ClientIDMode="Static">
      <asp:ScriptManager ID="sm1" runat="server" EnablePageMethods="true" />
      <asp:HiddenField ID="hfWorkflow" runat="server" ClientIDMode="Static" ValidateRequestMode="Disabled" />
      <asp:HiddenField ID="hfDefId" runat="server" ClientIDMode="Static" />
      
    <div class="layout">
      <!-- TOOLBOX -->
      <div class="panel">
        <div class="toolbox__header">
          <div class="toolbox__title">Caja de herramientas</div>
          <input id="search" class="toolbox__search" placeholder="Buscar nodos..." />
        </div>
        <div id="toolboxList" class="toolbox__list"></div>
      </div>

      <!-- CANVAS -->
      <div class="panel canvas-wrap">
        <div class="canvas-toolbar wf-toolbar">
    
            <!-- Nombre (más chico) -->
            <input id="txtNombreWf" name="txtNombreWf"
                   class="input wf-name"
                   placeholder="Nombre del workflow" />

            <!-- Grupo de acciones (encerrado/agrupado) -->
            <div class="wf-actions-box" role="group" aria-label="Acciones">
                <button type="button" class="btn" id="btnConectar">Conectar</button>
                <button type="button" class="btn" id="btnJSON">Export JSON</button>
                <button type="button" class="btn" id="btnSaveSql">Guardar en SQL</button>
                <button type="button" class="btn" id="btnClear">Limpiar</button>
                <button type="button" class="btn" id="btnToggleTest">Probar motor</button>
            </div>

            <!-- Definiciones (estilo bootstrap-like) -->
            <button type="button" id="btnIrDef" class="btn btn-outline-primary">
                Definiciones
            </button>

            <!-- Usuario como en WF_Gerente_Tareas -->
            <div class="wf-userpill">
                Usuario: <asp:Label ID="lblUser" runat="server" />
            </div>

        </div>
        <div id="canvas" class="canvas" tabindex="0" aria-label="Canvas de workflow">
          <svg id="edgesSvg" class="edges" viewBox="0 0 100 100" preserveAspectRatio="none">
            <defs>
              <marker id="arrow" markerWidth="10" markerHeight="7" refX="10" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#94a3b8"></polygon>
              </marker>
              <marker id="arrowSel" markerWidth="10" markerHeight="7" refX="10" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#2563eb"></polygon>
              </marker>
            </defs>
          </svg>
        </div>
      </div>

      <!-- INSPECTOR -->
      <div class="panel" id="inspector">
        <div class="inspector__header">
          <div class="inspector__title">Inspector</div>
          <div id="inspectorTitle" class="inspector__entity">Seleccioná un nodo o una arista</div>
          <div id="inspectorSub" class="inspector__sub"></div>
        </div>
        <div id="inspectorBody"></div>
      </div>
    </div>

    <!-- NUEVO: Probar motor en servidor (pegar JSON y ejecutar) -->
    <div class="panel" id="panelProbarMotor" style="margin:12px; display:none">
      <div class="inspector__header">
        <div class="inspector__title">Probar motor en servidor</div>
        <div class="inspector__entity">Pegá el JSON exportado y ejecutá el flujo del lado servidor (C#)</div>
      </div>

      <div class="section">
        <div class="label">JSON del workflow</div>
        <asp:TextBox ID="JsonServidor"
                     runat="server"
                     ClientIDMode="Static"
                     CssClass="textarea"
                     TextMode="MultiLine"
                     Rows="12"
                     Wrap="False"
                     ValidateRequestMode="Disabled" />
        <div class="hint">Sugerencia: hacé clic en <b>Export JSON</b> (arriba), luego <b>Pegar último JSON del visor</b>.</div>
        <div class="btn-row" style="margin-top:8px">
          <asp:Button ID="btnProbarMotor"
                      runat="server"
                      ClientIDMode="Static"
                      CssClass="btn"
                      Text="Ejecutar en servidor"
                      OnClientClick="if(window.captureWorkflow){window.captureWorkflow();} else if(window.buildWorkflow){document.getElementById('hfWorkflow').value = JSON.stringify(window.buildWorkflow());} "
                      OnClick="btnProbarMotor_Click" />
          <!-- Copia el contenido del visor (outText) al TextBox sin postback -->
          <asp:Button ID="btnPegarUltimoJson"
                      runat="server"
                      ClientIDMode="Static"
                      CssClass="btn"
                      Text="Pegar último JSON del visor"
                      OnClientClick="(function(){var t=document.getElementById('outText'); var a=document.getElementById('JsonServidor'); a.value=(t&&t.textContent)||'';})(); return false;" />
             <!-- Dibujar desde JSON -->
          <asp:Button ID="btnCargarEnCanvas"
                     runat ="server"
                     ClientIDMode="Static"
                     CssClass="btn"
                     Text="Cargar en canvas"
                     OnClientClick="WF_cargarJsonServidorEnCanvas(); return false;" />
        </div>
      </div>

      <div class="section">
        <div class="label">Logs (servidor)</div>
        <pre style="max-height:260px;overflow:auto"><asp:Literal ID="litLogs" runat="server" /></pre>
      </div>
    </div>
    <!-- Panel flotante de salida (JSON / C#) -->
    <div id="output" class="output" style="display:none">
      <div class="panel output__panel">
        <div class="output__bar">
          <div id="outTitle" class="output__title">Salida</div>
          <div class="btn-row">
            <button type="button" class="btn" id="btnCopy">Copiar</button>
            <button type="button" class="btn" id="btnCloseOut">Cerrar</button>
          </div>
        </div>
        <pre><code id="outText"></code></pre>
      </div>
    </div>
   </form>

    <!-- JS de la IU -->
    <script src="Scripts/workflow.catalog.js?v=dev6"></script>
    <script src="Scripts/workflow.templates.js?v=dev4"></script>    
    <script src="Scripts/workflow.ui.js"></script>
    <!-- Inspectores -->
    <script src="Scripts/inspectors/json.validator.js"></script>
    <script src="Scripts/inspectors/inspector.core.js"></script>
    <script src="Scripts/inspectors/inspector.file.read.js"></script>
    <script src="Scripts/inspectors/inspector.file.write.js"></script>    
    <script src="Scripts/inspectors/inspector.doc.extract.js"></script>
    <script src="Scripts/inspectors/inspector.edge.js"></script>
    <script src="Scripts/inspectors/inspector.http.request.js"></script>
    <script src="Scripts/inspectors/inspector.if.js"></script>
    <script src="Scripts/inspectors/inspector.logger.js"></script>
    <script src="Scripts/inspectors/inspector.human.task.js"></script>
    <script src="Scripts/inspectors/inspector.data.sql.js"></script>
    <script src="Scripts/inspectors/inspector.util.notify.js"></script>
    <script src="Scripts/inspectors/inspector.chat.notify.js"></script>
    <script src="Scripts/inspectors/inspector.queue.publish.js"></script>
    <script src="Scripts/inspectors/inspector.doc.entrada.js"></script>
    <script src="Scripts/inspectors/inspector.doc.load.js"></script>
    <script src="Scripts/inspectors/inspector.util.docTipo.resolve.js?v=dev5"></script>
    <script src="Scripts/inspectors/inspector.util.error.js"></script>
    <script src="Scripts/inspectors/inspector.ftp.put.js"></script>
    <script src="Scripts/inspectors/inspector.email.send.js"></script>

    <!-- (Opcional) Demos -->
    <script src="Scripts/workflow.demo.js"></script>

        <script type="text/javascript">
            // toma lo que está en el panel flotante (outText) o lo que genere el editor
            function copyWorkflowToHidden() {
                try {
                    var out = document.getElementById('outText');
                    var hf = document.getElementById('hfWorkflow');
                    if (out && hf) {
                        hf.value = out.textContent || out.innerText || '';
                    }
                } catch (e) { }
            }

            function wf_beforePostback() {
                var h = document.getElementById('hfWorkflow');
                if (h && window.WF_getJson) {
                    h.value = window.WF_getJson();
                }
                return true; // deja seguir el postback
            }

            // Cargar el JSON pegado en el panel inferior dentro del canvas
            function WF_cargarJsonServidorEnCanvas() {
                try {
                    console.log('WF_cargarJsonServidorEnCanvas: click');

                    var txt = document.getElementById('JsonServidor');
                    if (!txt) {
                        alert('No encuentro el TextBox JsonServidor.');
                        return;
                    }

                    var json = (txt.value || '').trim();
                    console.log('WF_cargarJsonServidorEnCanvas: longitud JSON =', json.length);

                    if (!json) {
                        alert('Pegá primero el JSON del workflow en el cuadro "JSON del workflow".');
                        return;
                    }

                    if (window.WF_loadFromJson) {
                        console.log('WF_cargarJsonServidorEnCanvas: llamando WF_loadFromJson...');
                        window.WF_loadFromJson(json);
                    } else {
                        alert('No existe window.WF_loadFromJson. Verificá que esté definido en Scripts/workflow.ui.js.');
                    }
                } catch (e) {
                    console.error('Error en WF_cargarJsonServidorEnCanvas:', e);
                    alert('Error al cargar el JSON en el canvas: ' + e.message);
                }
            }

            // NUEVO: mostrar/ocultar panel de "Probar motor en servidor"
            (function () {
                var btn = document.getElementById('btnToggleTest');
                var panel = document.getElementById('panelProbarMotor');
                if (!btn || !panel) return;

                btn.addEventListener('click', function () {
                    var visible = panel.style.display !== 'none';
                    panel.style.display = visible ? 'none' : 'block';
                    if (!visible) {
                        // cuando se muestra, lo llevamos a la vista
                        try {
                            panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
                        } catch (e) { /* compatibilidad viejos browsers */ }
                    }
                });
            })();

            (function () {
                var b = document.getElementById('btnIrDef');
                if (!b) return;

                b.addEventListener('click', function () {
                    var id = document.getElementById('hfDefId')?.value;
                    if (id && id !== '') {
                        window.location.href = 'WF_Definiciones.aspx?defId=' + encodeURIComponent(id);
                    } else {
                        window.location.href = 'WF_Definiciones.aspx';
                    }
                });
            })();

        </script>

 </body>
</html>
