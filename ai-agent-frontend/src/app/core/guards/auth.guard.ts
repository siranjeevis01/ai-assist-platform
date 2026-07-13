import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { toObservable } from '@angular/core/rxjs-interop';
import { filter, take, map } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  private loading$!: ReturnType<typeof toObservable<boolean>>;

  constructor(private auth: AuthService, private router: Router) {
    this.loading$ = toObservable(this.auth.loading);
  }

  async canActivate(): Promise<boolean> {
    if (this.auth.isAuthenticated()) {
      return true;
    }
    if (this.auth.loading()) {
      const loaded = await firstValueFrom(
        this.loading$.pipe(
          filter(loading => !loading),
          take(1),
          map(() => this.auth.isAuthenticated())
        )
      );
      if (loaded) return true;
      this.router.navigate(['/login']);
      return false;
    }
    this.router.navigate(['/login']);
    return false;
  }
}
