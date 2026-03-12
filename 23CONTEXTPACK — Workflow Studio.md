# CONTEXTPACK — Workflow Studio (Estado actual Gerencia)

## 🎯 Objetivo del ajuste reciente

Dejar perfectamente separadas las bandejas:

* ✅ **Pendientes = estado actual**
* ✅ **Cerradas = estado final**
* ❌ nunca mezcladas

Esto aplica específicamente a:

👉 `WF_Gerente_Tareas.aspx`
👉 SP:

* `WF_Gerente_Tareas_Pendientes_MiAlcance`
* `WF_Gerente_Tareas_Cerradas_Mis`

---

## ✅ Criterio funcional definitivo (acordado)

### 🔹 PENDIENTES (Gerencia)

Representa:

👉 el estado actual del circuito

Debe:

✔ mostrar instancias en curso
✔ mostrar la tarea actual
✔ mostrar quién la tiene ahora
✔ no importa lo que pasó antes

Ejemplo real:

* U5 rechaza
* vuelve a U2

👉 en grilla debe verse U2
👉 no U5

🔴 Error anterior: mostraba al que ya intervino.

✔ Solución aplicada:

Agregar columna:

```
ResponsableActual =
COALESCE(NULLIF(AsignadoA,''), NULLIF(UsuarioAsignado,''), RolDestino)
```

👉 ahora muestra quién la tiene realmente.

---

### 🔹 CERRADAS (Gerencia)

Representa:

👉 el resultado final del circuito

Debe:

✔ mostrar SOLO instancias finalizadas
✔ una sola fila por instancia
✔ la última tarea cerrada real
✔ sin filtrar por usuario/rol del que mira
✔ solo validar permiso gerencial

👉 NO listar tareas cerradas intermedias
👉 NO listar instancias en curso

---

## ✅ Resultado esperado (validado con pruebas)

### ✔ Instancia en curso

👉 aparece en **Pendientes**
👉 ❌ NO aparece en **Cerradas**

---

### ✔ Instancia finalizada

👉 aparece en **Cerradas**
👉 ❌ desaparece de **Pendientes**

👉 comportamiento limpio y lógico.

---

## ✅ SP final aplicado — CERRADAS

Características:

* controla solo permiso gerencial
* filtra por instancia finalizada
* toma última tarea cerrada real

Implementación:

```
ROW_NUMBER() OVER (PARTITION BY WF_InstanciaId ORDER BY FechaCierre DESC)
+
JOIN WF_Instancia con Estado='Finalizado'
```

---

## 🧠 Conclusión conceptual lograda

La vista gerencial quedó clara:

| Solapa     | Representa        |
| ---------- | ----------------- |
| Pendientes | lo que pasa ahora |
| Cerradas   | cómo terminó      |

👉 nunca mezcladas
👉 lectura inmediata

---

## 🔜 Pendiente para próxima sesión

### UX sugerido (no implementado aún)

Agregar en Cerradas:

👉 columna **"Cerrada por"**

Se obtiene desde:

```
Datos.data.cerradoPor (JSON WF_Tarea)
```

👉 mejora muchísimo la lectura gerencial.

---

## 📍 Estado del sistema al cerrar sesión

✔ SP Pendientes correcto
✔ SP Cerradas correcto
✔ grilla consistente
✔ sin confusión funcional

👉 listo para continuar en próxima sesión
