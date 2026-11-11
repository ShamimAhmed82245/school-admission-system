# Assumptions

The assignment specifies the required admission workflow and API behavior but does not prescribe the database schema.

1. **Database Structure:** Used two main tables, `classes` and `holds`, instead of creating a separate students table. The student birth registration number is sufficient to identify students for the required operations.

2. **Seat Availability:** Added an `available_seats` column to the `classes` table rather than calculating available seats from hold records on every request. This supports efficient seat tracking and atomic seat reservation.
