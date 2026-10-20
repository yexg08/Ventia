import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';

/**
 * nombre del archivo que manda el servidor en Content-Disposition
 * prefiere filename* (admite tildes) y si no viene usa filename o el nombre por defecto
 */
export function fileNameFrom(header: string | null, fallback: string): string {
  if (!header) return fallback;
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim());
    } catch {
      // nombre mal codificado (se intenta con filename)
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : fallback;
}

/**
 * descarga un archivo protegido (un enlace normal no lleva el token de la sesion)
 * lo pide con HttpClient y lo guarda con un enlace temporal
 */
export function downloadFile(http: HttpClient, url: string, params: HttpParams, fallbackName: string): Observable<void> {
  return http.get(url, { params, responseType: 'blob', observe: 'response' }).pipe(
    map((response) => {
      const name = fileNameFrom(response.headers.get('Content-Disposition'), fallbackName);
      const href = URL.createObjectURL(response.body ?? new Blob());
      const link = document.createElement('a');
      link.href = href;
      link.download = name;
      link.click();
      // se libera despues de que el navegador empezo la descarga
      setTimeout(() => URL.revokeObjectURL(href), 1000);
    }),
  );
}
