import { HttpInterceptorFn, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { timer, throwError, Observable } from 'rxjs';
import { mergeMap } from 'rxjs/operators';

const retryableStatusCodes = [429, 503];

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  let retryCount = 0;
  const MAX_RETRIES = 2;

  return next(req).pipe(
    mergeMap((event: HttpEvent<unknown>): Observable<HttpEvent<unknown>> => {
      if (event instanceof HttpErrorResponse && retryableStatusCodes.includes(event.status) && retryCount < MAX_RETRIES) {
        retryCount++;
        const retryAfter = event.error?.retryAfter || 3;
        const delay = Math.min(retryAfter * 1000, 15000);
        return timer(delay) as any;
      }
      return [event] as any;
    })
  );
};
