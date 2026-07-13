import { HttpInterceptorFn, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { timer, Observable } from 'rxjs';
import { mergeMap } from 'rxjs/operators';

const retryableStatusCodes = [429, 503];

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  let retryCount = 0;
  const MAX_RETRIES = 2;

  return next(req).pipe(
    mergeMap((event: HttpEvent<unknown>): Observable<HttpEvent<unknown>> => {
      if (event instanceof HttpErrorResponse && retryableStatusCodes.includes(event.status) && retryCount < MAX_RETRIES) {
        retryCount++;
        const retryAfter = event.headers.get('Retry-After');
        const delay = retryAfter ? Math.min(parseInt(retryAfter, 10) * 1000, 15000) : 3000;
        return timer(delay).pipe(
          mergeMap(() => next(req))
        );
      }
      return [event] as any;
    })
  );
};
