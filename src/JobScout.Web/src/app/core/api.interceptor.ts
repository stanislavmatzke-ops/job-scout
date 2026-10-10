import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const READ_ONLY = ['GET', 'HEAD', 'OPTIONS'];

/**
 * Every state-changing API call carries "X-CSRF: 1" (ADR-0011).
 * A 401 anywhere = the session expired → back to the login page, then back where the user was.
 * A 403 = the access changed meanwhile (e.g. the admin suspended the account) → reload "who am I",
 * so the suspension banner and the disabled AI buttons appear right away.
 */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (req.url.startsWith('/api/') && !READ_ONLY.includes(req.method)) {
    req = req.clone({ setHeaders: { 'X-CSRF': '1' } });
  }

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && req.url !== '/api/me') {
        auth.markSignedOut();
        router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      if (error instanceof HttpErrorResponse && error.status === 403 && req.url.startsWith('/api/') && req.url !== '/api/me') {
        void auth.load(true).catch(() => undefined);
      }
      return throwError(() => error);
    }),
  );
};
