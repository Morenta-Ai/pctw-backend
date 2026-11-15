namespace Rethread.Api.Data.Entities;

// Stored and serialised as camelCase strings (see CamelCaseEnumConverter), matching the contracts.

public enum AccountStatus { Active, Suspended }

public enum StaffRole { Admin, CallCentre }

public enum Language { En, Si, Ta }

public enum Audience { Customer, Collector, Staff }

public enum AddressLabel { Home, Work, Other }

public enum FamilyRelation { Self, Spouse, Son, Daughter, Mother, Father, Sibling, Other }

public enum Gender { Female, Male, Other }

public enum AgeGroup { Adult, Teen, Kid }

/// <summary>The portal's set. The user and collector apps see translated values; see ARCHITECTURE.md.</summary>
public enum PickupStatus { Pending, Assigned, EnRoute, Arrived, Collected, DeliveredToHub, Cancelled }

public enum PickupSource { App, CallCentre }

public enum PickupMethod { Bulk, Itemised }

public enum BulkMode { Weight, Pieces }

public enum Slot { Morning, Afternoon, Evening }

public enum ItemCondition { Good, Fair, Worn, Damaged }

public enum VehicleType { ThreeWheeler, Motorbike, Van, Lorry }

public enum WeightSource { Scale, Manual }

public enum PhotoKind { Customer, Bag }

public enum AssignmentStatus { Offered, Accepted, Declined, Expired, Withdrawn }

public enum DeclineReason { VehicleFull, TooFar, EndingShift, VehicleProblem, Other }

public enum TransactionType { PickupEarning, BankTransfer, Voucher, Donation, ShopOrder }

public enum TransactionStatus { Pending, Completed, Failed }

public enum RedemptionType { Cash, Voucher, Donation }

public enum RedemptionStatus { Pending, Paid, Rejected, Completed }

public enum ProductCategory { Women, Men, Kids, Accessories }

public enum FulfilmentType { HomeDelivery, ClickAndCollect }

public enum PaymentMethod { Card, Wallet }

public enum PaymentStatus { Unpaid, Paid, Refunded, Failed }

public enum OrderStatus { Placed, Confirmed, OutForDelivery, ReadyAtVendor, Completed, Cancelled }
