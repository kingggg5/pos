import { Injectable, signal, computed } from '@angular/core';
import { AuthResponse } from '../models/pos.models';

const AUTH_STORAGE_KEY = 'smart_pos_auth_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
	readonly currentUser = signal<AuthResponse | null>(this.loadStoredUser());

	readonly isAuthenticated = computed(() => !!this.currentUser());
	readonly tenantName = computed(() => this.currentUser()?.tenantName || 'No Store Selected');
	readonly tenantSlug = computed(() => this.currentUser()?.tenantSlug || 'default');
	readonly token = computed(() => this.currentUser()?.token || '');
	readonly isOwner = computed(() => this.currentUser()?.role === 'Owner');

	setSession(authData: AuthResponse): void {
		this.currentUser.set(authData);
		localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(authData));
	}

	logout(): void {
		this.currentUser.set(null);
		localStorage.removeItem(AUTH_STORAGE_KEY);
	}

	private loadStoredUser(): AuthResponse | null {
		try {
			const data = localStorage.getItem(AUTH_STORAGE_KEY);
			return data ? JSON.parse(data) : null;
		} catch {
			return null;
		}
	}
}
