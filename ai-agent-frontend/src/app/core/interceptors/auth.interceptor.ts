import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { ApiService } from '../services/api.service';

let isRefreshing = false;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const api = inject(ApiService);
  const token = localStorage.getItem('access_token');

  let authReq = req;
  if (token) {
    authReq = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !isRefreshing && !req.url.includes('/Auth/refresh') && !req.url.includes('/Auth/login') && !req.url.includes('/Auth/register')) {
        isRefreshing = true;
        const refreshToken = localStorage.getItem('refresh_token');
        if (refreshToken) {
          return api.refreshToken(refreshToken).pipe(
            switchMap((res: { token: string }) => {
              isRefreshing = false;
              localStorage.setItem('access_token', res.token);
              const newReq = req.clone({ setHeaders: { Authorization: `Bearer ${res.token}` } });
              return next(newReq);
            }),
            catchError(() => {
              isRefreshing = false;
              localStorage.removeItem('access_token');
              localStorage.removeItem('refresh_token');
              router.navigate(['/login']);
              return throwError(() => error);
            })
          );
        }
        isRefreshing = false;
        localStorage.removeItem('access_token');
        localStorage.removeItem('refresh_token');
        router.navigate(['/login']);
      }
      return throwError(() => error);
    })
  );
};
