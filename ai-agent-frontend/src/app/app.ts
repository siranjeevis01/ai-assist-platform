import { Component, signal } from '@angular/core';
import { RouterOutlet, Router, NavigationStart, NavigationEnd, NavigationError } from '@angular/router';
import { NgIf } from '@angular/common';
import { ToastComponent } from './shared/toast/toast.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastComponent, NgIf],
  template: `
    <div class="route-loader" *ngIf="loading()"><div class="route-loader-bar"></div></div>
    <router-outlet />
    <app-toast />
  `,
  styles: [`
    .route-loader { position: fixed; top: 0; left: 0; right: 0; z-index: 10000; height: 3px; }
    .route-loader-bar { height: 100%; background: linear-gradient(90deg, #667eea, #764ba2); animation: routeProgress 1s ease-in-out infinite; }
    @keyframes routeProgress { 0% { width: 0; } 50% { width: 70%; } 100% { width: 100%; } }
  `]
})
export class App {
  loading = signal(false);

  constructor(router: Router) {
    router.events.subscribe(e => {
      if (e instanceof NavigationStart) this.loading.set(true);
      else if (e instanceof NavigationEnd || e instanceof NavigationError) this.loading.set(false);
    });
  }
}
