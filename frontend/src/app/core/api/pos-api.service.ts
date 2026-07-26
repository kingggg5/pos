import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
	AuditLog,
	AuthResponse,
	CashShift,
	Category,
	CloseCashShiftRequest,
	CreateCustomerRequest,
	CreateOrderRequest,
	CreateStaffRequest,
	Customer,
	LoyaltyTransaction,
	OpenCashShiftRequest,
	Order,
	OrderQuote,
	OrderQuoteRequest,
	OrderReversal,
	OrderReversalRequest,
	PartialRefundOrderRequest,
	PlatformDashboard,
	Product,
	Promotion,
	SavePromotionRequest,
	StaffUser,
	StoreSettings,
	UpdateStaffRequest,
	UpdateStoreSettingsRequest,
	PromotionValidation,
	ValidatePromotionRequest,
	ZReportSummary
} from '../models/pos.models';

const API_BASE = '/api';

@Injectable({ providedIn: 'root' })
export class PosApiService {
	private readonly http = inject(HttpClient);

	/* ── Auth ────────────────────────────────────── */

	registerStore(storeName: string, storeSlug: string, ownerEmail: string, ownerPassword: string, ownerFullName: string): Observable<AuthResponse> {
		return this.http.post<AuthResponse>(`${API_BASE}/auth/register-store`, {
			storeName,
			storeSlug,
			ownerEmail,
			ownerPassword,
			ownerFullName
		});
	}

	login(email: string, password: string): Observable<AuthResponse> {
		return this.http.post<AuthResponse>(`${API_BASE}/auth/login`, { email, password });
	}

	/* ── Products ────────────────────────────────── */

	getProducts(search?: string, categoryId?: number): Observable<Product[]> {
		let url = `${API_BASE}/products`;
		const params: string[] = [];
		if (search) params.push(`search=${encodeURIComponent(search)}`);
		if (categoryId) params.push(`categoryId=${categoryId}`);
		if (params.length > 0) url += `?${params.join('&')}`;
		return this.http.get<Product[]>(url);
	}

	getCategories(): Observable<Category[]> {
		return this.http.get<Category[]>(`${API_BASE}/products/categories`);
	}

	saveProduct(productData: Partial<Product>): Observable<Product> {
		if (productData.id && productData.id > 0) {
			return this.http.put<Product>(`${API_BASE}/products/${productData.id}`, productData);
		}
		return this.http.post<Product>(`${API_BASE}/products`, productData);
	}

	deleteProduct(productId: number): Observable<void> {
		return this.http.delete<void>(`${API_BASE}/products/${productId}`);
	}

	/* ── Orders ──────────────────────────────────── */

	checkout(request: CreateOrderRequest): Observable<Order> {
		return this.http.post<Order>(`${API_BASE}/orders/checkout`, request);
	}

	getOrders(): Observable<Order[]> {
		return this.http.get<Order[]>(`${API_BASE}/orders`);
	}

	voidOrder(orderId: number, request: OrderReversalRequest): Observable<OrderReversal> {
		return this.http.post<OrderReversal>(`${API_BASE}/orders/${orderId}/void`, request);
	}

	refundOrder(orderId: number, request: PartialRefundOrderRequest): Observable<OrderReversal> {
		return this.http.post<OrderReversal>(`${API_BASE}/orders/${orderId}/refund-items`, request);
	}

	exportOrdersCsv(): Observable<Blob> {
		return this.http.get(`${API_BASE}/orders/export`, { responseType: 'blob' });
	}

	getOrderQuote(request: OrderQuoteRequest): Observable<OrderQuote> {
		return this.http.post<OrderQuote>(`${API_BASE}/orders/quote`, request);
	}

	/* ── Cash shifts ─────────────────────────────────────── */

	getCurrentCashShift(): Observable<CashShift | null> {
		return this.http.get<CashShift | null>(`${API_BASE}/cash-shifts/current`);
	}

	getCashShifts(): Observable<CashShift[]> {
		return this.http.get<CashShift[]>(`${API_BASE}/cash-shifts?limit=50`);
	}

	openCashShift(request: OpenCashShiftRequest): Observable<CashShift> {
		return this.http.post<CashShift>(`${API_BASE}/cash-shifts/open`, request);
	}

	closeCashShift(shiftId: number, request: CloseCashShiftRequest): Observable<CashShift> {
		return this.http.post<CashShift>(`${API_BASE}/cash-shifts/${shiftId}/close`, request);
	}

	/* ── Customers & loyalty ─────────────────────────────── */

	searchCustomer(phone: string): Observable<Customer> {
		return this.http.get<Customer>(
			`${API_BASE}/customers/search?phone=${encodeURIComponent(phone)}`
		);
	}

	getCustomers(search: string = '', limit: number = 100): Observable<Customer[]> {
		const query = new URLSearchParams({ search, limit: String(limit) });
		return this.http.get<Customer[]>(`${API_BASE}/customers?${query.toString()}`);
	}

	createCustomer(request: CreateCustomerRequest): Observable<Customer> {
		return this.http.post<Customer>(`${API_BASE}/customers`, request);
	}

	getCustomerPoints(customerId: number): Observable<LoyaltyTransaction[]> {
		return this.http.get<LoyaltyTransaction[]>(
			`${API_BASE}/customers/${customerId}/points?limit=50`
		);
	}

	/* ── Promotions & coupons ────────────────────────────── */

	getPromotions(): Observable<Promotion[]> {
		return this.http.get<Promotion[]>(`${API_BASE}/promotions`);
	}

	createPromotion(request: SavePromotionRequest): Observable<Promotion> {
		return this.http.post<Promotion>(`${API_BASE}/promotions`, request);
	}

	updatePromotion(id: number, request: SavePromotionRequest): Observable<Promotion> {
		return this.http.put<Promotion>(`${API_BASE}/promotions/${id}`, request);
	}

	deletePromotion(id: number): Observable<void> {
		return this.http.delete<void>(`${API_BASE}/promotions/${id}`);
	}

	validatePromotion(request: ValidatePromotionRequest): Observable<PromotionValidation> {
		return this.http.post<PromotionValidation>(`${API_BASE}/promotions/validate`, request);
	}

	/* ── Reports ─────────────────────────────────── */

	getZReportSummary(): Observable<ZReportSummary> {
		return this.http.get<ZReportSummary>(`${API_BASE}/reports/summary`);
	}

	getAuditLogs(limit: number = 100): Observable<AuditLog[]> {
		return this.http.get<AuditLog[]>(`${API_BASE}/reports/audit-logs?limit=${limit}`);
	}

	getPlatformDashboard(): Observable<PlatformDashboard> {
		return this.http.get<PlatformDashboard>(`${API_BASE}/reports/platform-dashboard`);
	}

	/* ── Staff / Users ───────────────────────────── */

	getStaffUsers(): Observable<StaffUser[]> {
		return this.http.get<StaffUser[]>(`${API_BASE}/users`);
	}

	createStaff(request: CreateStaffRequest): Observable<StaffUser> {
		return this.http.post<StaffUser>(`${API_BASE}/users`, request);
	}

	updateStaff(id: number, request: UpdateStaffRequest): Observable<StaffUser> {
		return this.http.put<StaffUser>(`${API_BASE}/users/${id}`, request);
	}

	deleteStaff(id: number): Observable<void> {
		return this.http.delete<void>(`${API_BASE}/users/${id}`);
	}

	/* ── Store Settings ──────────────────────────── */

	getStoreSettings(): Observable<StoreSettings> {
		return this.http.get<StoreSettings>(`${API_BASE}/storesettings`);
	}

	updateStoreSettings(request: UpdateStoreSettingsRequest): Observable<StoreSettings> {
		return this.http.put<StoreSettings>(`${API_BASE}/storesettings`, request);
	}
}
