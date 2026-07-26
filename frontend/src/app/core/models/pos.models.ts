export type UserRole = 'Owner' | 'Manager' | 'Cashier';
export type OrderStatus = 'Completed' | 'Cancelled' | 'Refunded' | 'PartiallyRefunded';
export type PaymentMethod = 'Cash' | 'PromptPay' | 'CreditCard';
export type CashShiftStatus = 'Open' | 'Closed';
export type PromotionDiscountType = 'Percentage' | 'FixedAmount';

export interface AuthResponse {
	userId: number;
	email: string;
	fullName: string;
	role: UserRole;
	tenantId: number;
	tenantName: string;
	tenantSlug: string;
	token: string;
}

export interface StoreSettings {
	id: number;
	name: string;
	slug: string;
	qrCodeUrl: string | null;
	vatRate: number;
	serviceChargeRate: number;
	receiptHeaderNote: string;
	receiptFooterNote: string;
	businessTimeZoneId: string;
}

export interface UpdateStoreSettingsRequest {
	qrCodeUrl: string | null;
	vatRate: number;
	serviceChargeRate: number;
	receiptHeaderNote: string;
	receiptFooterNote: string;
	businessTimeZoneId?: string | null;
}

export interface Category {
	id: number;
	name: string;
	icon: string;
}

export interface Product {
	id: number;
	categoryId: number;
	categoryName: string;
	barcode: string;
	name: string;
	price: number;
	cost: number;
	stockQuantity: number;
	minimumStock: number;
	unit: string;
	imageUrl?: string;
	isActive: boolean;
	isLowStock: boolean;
}

export interface CartItem {
	product: Product;
	quantity: number;
	subTotal: number;
}

export interface CreateOrderRequest {
	items: { productId: number; quantity: number }[];
	discountAmount: number;
	paidAmount: number;
	paymentMethod: PaymentMethod;
	idempotencyKey: string;
	customerPhone?: string | null;
	couponCode?: string | null;
	loyaltyPointsToRedeem?: number;
}

export interface OrderQuoteRequest {
	items: { productId: number; quantity: number }[];
	discountAmount: number;
	customerPhone?: string | null;
	couponCode?: string | null;
	loyaltyPointsToRedeem?: number;
}

export interface OrderQuote {
	subTotalAmount: number;
	manualDiscountAmount: number;
	couponDiscountAmount: number;
	loyaltyDiscountAmount: number;
	totalDiscountAmount: number;
	serviceChargeAmount: number;
	vatAmount: number;
	totalAmount: number;
	customerId: number | null;
	customerName: string | null;
	customerPointsBalance: number;
	loyaltyPointsRedeemed: number;
	loyaltyPointsEarned: number;
	couponCode: string | null;
}

export interface OrderItem {
	id: number;
	productId: number;
	productName: string;
	barcode: string;
	unitPrice: number;
	quantity: number;
	subTotal: number;
	refundedQuantity: number;
	refundableQuantity: number;
}

export interface Order {
	id: number;
	orderNo: string;
	totalAmount: number;
	discountAmount: number;
	paidAmount: number;
	changeAmount: number;
	paymentMethod: PaymentMethod;
	status: OrderStatus;
	cashierName: string;
	createdAt: string;
	items: OrderItem[];
	subTotalAmount?: number;
	serviceChargeAmount?: number;
	vatAmount?: number;
	manualDiscountAmount?: number;
	customerId?: number | null;
	customerName?: string | null;
	customerPhone?: string | null;
	couponCode?: string | null;
	couponDiscountAmount?: number;
	loyaltyDiscountAmount?: number;
	loyaltyPointsEarned?: number;
	loyaltyPointsRedeemed?: number;
	cashShiftId?: number | null;
	version?: number;
	totalRefundedAmount: number;
}

export interface CashShift {
	id: number;
	status: CashShiftStatus;
	openingCash: number;
	cashSalesAmount: number;
	cashRefundAmount: number;
	expectedCash: number;
	closingCash: number | null;
	difference: number | null;
	openedByUserId: number;
	openedByName: string;
	closedByUserId: number | null;
	closedByName: string | null;
	openedAt: string;
	closedAt: string | null;
	openingNote: string | null;
	closingNote: string | null;
	version: number;
}

export interface OpenCashShiftRequest {
	openingCash: number;
	openingNote?: string;
	idempotencyKey: string;
}

export interface CloseCashShiftRequest {
	closingCash: number;
	closingNote?: string;
	idempotencyKey: string;
}

export interface Customer {
	id: number;
	phone: string;
	name: string;
	email: string | null;
	pointsBalance: number;
	lifetimePointsEarned: number;
	lifetimePointsRedeemed: number;
	createdAt: string;
	updatedAt: string;
	version: number;
}

export interface CreateCustomerRequest {
	phone: string;
	name: string;
	email?: string | null;
}

export interface LoyaltyTransaction {
	id: number;
	type: 'Earn' | 'Redeem' | 'EarnReversal' | 'RedeemReversal' | 'Adjustment';
	pointsChange: number;
	balanceAfter: number;
	orderId: number | null;
	orderNo: string | null;
	description: string;
	createdAt: string;
}

export interface Promotion {
	id: number;
	code: string;
	name: string;
	description: string | null;
	discountType: PromotionDiscountType;
	value: number;
	minimumOrderAmount: number;
	maximumDiscountAmount: number | null;
	usageLimit: number | null;
	usageCount: number;
	validFrom: string | null;
	validUntil: string | null;
	isActive: boolean;
	version: number;
}

export interface SavePromotionRequest {
	code: string;
	name: string;
	description?: string | null;
	discountType: PromotionDiscountType;
	value: number;
	minimumOrderAmount: number;
	maximumDiscountAmount?: number | null;
	usageLimit?: number | null;
	validFrom?: string | null;
	validUntil?: string | null;
	isActive: boolean;
}

export interface ValidatePromotionRequest {
	code: string;
	subTotal: number;
}

export interface PromotionValidation {
	isValid: boolean;
	code: string;
	discountType: PromotionDiscountType | null;
	value: number | null;
	discountAmount: number;
	message: string;
	minimumOrderAmount: number;
	maximumDiscountAmount: number | null;
	validFrom: string | null;
	validUntil: string | null;
}

export interface OrderReversalRequest {
	reason: string;
	idempotencyKey: string;
}

export interface PartialRefundOrderRequest extends OrderReversalRequest {
	items: {
		orderItemId: number;
		quantity: number;
	}[];
}

export interface OrderReversalItem {
	orderItemId: number;
	productId: number;
	productName: string;
	quantity: number;
	subTotalAmount: number;
	manualDiscountAmount: number;
	couponDiscountAmount: number;
	loyaltyDiscountAmount: number;
	serviceChargeAmount: number;
	vatAmount: number;
	totalAmount: number;
}

export interface OrderReversal {
	id: number;
	orderId: number;
	orderNo: string;
	type: 'Void' | 'Refund' | 'PartialRefund';
	amount: number;
	stockRestored: boolean;
	reason: string;
	idempotencyKey: string;
	performedBy: string;
	processedAt: string;
	subTotalAmount: number;
	manualDiscountAmount: number;
	couponDiscountAmount: number;
	loyaltyDiscountAmount: number;
	serviceChargeAmount: number;
	vatAmount: number;
	isFullOrderReversal: boolean;
	loyaltyPointsEarnedReversed: number;
	loyaltyPointsRedeemedRestored: number;
	couponUsageReleased: boolean;
	items: OrderReversalItem[];
}

export interface ZReportSummary {
	todayTotalRevenue: number;
	todayTotalOrders: number;
	totalProductsCount: number;
	lowStockProductsCount: number;
	averageOrderValue: number;
	topSellingProducts: Product[];
	businessTimeZoneId: string;
	businessDate: string;
	businessDayStartUtc: string;
	businessDayEndUtc: string;
	todayGrossSales: number;
	todayRefundAmount: number;
	todayVoidAmount: number;
	todayReversalEvents: number;
}

export interface StaffUser {
	id: number;
	email: string;
	fullName: string;
	employeeCode: string;
	positionTitle: string;
	role: UserRole;
	canProcessCheckout: boolean;
	canManageProducts: boolean;
	canViewReports: boolean;
	canManageUsers: boolean;
	createdAt: string;
}

export interface CreateStaffRequest {
	email: string;
	password: string;
	fullName: string;
	employeeCode: string;
	positionTitle: string;
	role: UserRole;
	canProcessCheckout: boolean;
	canManageProducts: boolean;
	canViewReports: boolean;
	canManageUsers: boolean;
}

export interface UpdateStaffRequest {
	fullName: string;
	employeeCode: string;
	positionTitle: string;
	role: UserRole;
	canProcessCheckout: boolean;
	canManageProducts: boolean;
	canViewReports: boolean;
	canManageUsers: boolean;
}

export interface AuditLog {
	id: number;
	action: string;
	category: string;
	performedBy: string;
	details: string;
	createdAt: string;
}

export interface StoreSummary {
	tenantId: number;
	name: string;
	slug: string;
	plan: string;
	usersCount: number;
	productsCount: number;
	ordersCount: number;
	totalRevenue: number;
	createdAt: string;
}

export interface CategoryDistribution {
	categoryName: string;
	productsCount: number;
	totalSoldQuantity: number;
}

export interface PlatformDashboard {
	totalStoresCount: number;
	totalPlatformRevenue: number;
	totalProductsCount: number;
	totalOrdersCount: number;
	totalUsersCount?: number;
	totalVisitsCount?: number;
	stores: StoreSummary[];
	categories: CategoryDistribution[];
	recentLogs: AuditLog[];
}

export type AppLocale = 'en' | 'th';

export interface TranslationMap {
	[key: string]: string;
}
