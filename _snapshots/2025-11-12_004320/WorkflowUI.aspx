<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WorkflowUI.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WorkflowUI" %>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Workflow UI</title>
  <meta charset="utf-8" />
  <link rel="stylesheet" href="Styles/workflow.ui.css" />
</head>
<body>
  <form id="form1" runat="server" ClientIDMode="Static">
      <asp:ScriptManager ID="sm1" runat="server" EnablePageMethods="true" />
      <asp:HiddenField ID="hfWorkflow" runat="server" ClientIDMode="Static" />
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
        <div class="canvas-toolbar">
          <button type="button" class="btn" id="btnDemo">Demo</button>
          <button type="button" class="btn" id="btnConectar">Conectar</button>
          <button type="button" class="btn" id="btnJSON">Export JSON</button>
          <button type="button" class="btn" id="btnCS">Export C#</button>
          <button type="button" class="btn" id="btnSaveSql">Guardar en SQL</button>
          <button type="button" class="btn" id="btnClear">Limpiar</button>
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
    <div class="panel" style="margin:12px">
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
  <script src="Scripts/workflow.catalog.js"></script>
  <script src="Scripts/workflow.ui.js"></script>
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
  </script>
 </body>
</html>
