import { Injectable, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from './api.service';
import { User, LoginRequest, RegisterRequest } from '../models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private tokenKey = 'access_token';
  private refreshKey = 'refresh_token';

  user = signal<User | null>(null);
  loading = signal(true);

  isAuthenticated = computed(() => this.user() !== null);

  constructor(
    private api: ApiService,
    private router: Router,
    private toast: ToastService
  ) {
    this.loadUser();
  }

  private loadUser(): void {
    const token = localStorage.getItem(this.tokenKey);
    if (token) {
      this.api.getCurrentUser().subscribe({
        next: (user) => {
          this.user.set(user);
          this.loading.set(false);
        },
        error: () => {
          this.clearTokens();
          this.loading.set(false);
          this.router.navigate(['/login']);
        }
      });
    } else {
      this.loading.set(false);
    }
  }

  login(req: LoginRequest): Promise<boolean> {
    return new Promise((resolve) => {
      this.api.login(req).subscribe({
        next: (res) => {
          localStorage.setItem(this.tokenKey, res.token);
          if (res.refreshToken) localStorage.setItem(this.refreshKey, res.refreshToken);
          this.api.getCurrentUser().subscribe({
            next: (user) => {
              this.user.set(user);
              this.toast.success('Welcome back!');
              resolve(true);
            },
            error: () => resolve(false)
          });
        },
        error: () => resolve(false)
      });
    });
  }

  register(req: RegisterRequest): Promise<boolean> {
    return new Promise((resolve) => {
      this.api.register(req).subscribe({
        next: (res) => {
          localStorage.setItem(this.tokenKey, res.token);
          if (res.refreshToken) localStorage.setItem(this.refreshKey, res.refreshToken);
          this.api.getCurrentUser().subscribe({
            next: (user) => {
              this.user.set(user);
              this.toast.success('Account created successfully!');
              resolve(true);
            },
            error: () => resolve(false)
          });
        },
        error: () => resolve(false)
      });
    });
  }

  logout(): void {
    this.api.logout().subscribe({
      next: () => this.toast.info('Logged out'),
      error: () => {}
    });
    this.clearTokens();
    this.user.set(null);
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(this.refreshKey);
  }

  clearTokens(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.refreshKey);
  }
}
