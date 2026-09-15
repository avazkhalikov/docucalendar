namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// A ready-made way of taking appointments for one kind of business: the questions, the services
/// with their lengths, the instructions, and the appointment lengths that suit them. Chosen on the
/// Calendars page, it fills the form; the owner adjusts and saves. Nothing here is applied by
/// itself.
/// </summary>
/// <param name="Key">Stable identifier, safe in a URL.</param>
/// <param name="Name">What the picker shows.</param>
/// <param name="Blurb">One sentence on who it is for.</param>
/// <param name="SlotMinutes">The default appointment length the template proposes.</param>
/// <param name="MaxMinutes">The longest the template needs — every service fits inside it.</param>
/// <param name="ScriptJson">The script itself, in the shape <see cref="BookingScript.Parse"/> reads.</param>
public sealed record BookingTemplate(string Key, string Name, string Blurb, int SlotMinutes, int MaxMinutes, string ScriptJson);

/// <summary>
/// The ten kinds of business a phone assistant most often books for. Written for the phone: an
/// instruction is something the assistant can act on in a call, a question is one a caller can
/// answer in a sentence, and every safety rule ("do not book chest pain — send them to emergency")
/// comes first. Validated as a unit by BookingTemplatesTests, so a template can never offer a
/// script the calendar would refuse.
/// </summary>
public static class BookingTemplates
{
    public static readonly IReadOnlyList<BookingTemplate> All = new[]
    {
        new BookingTemplate("dental", "Dental clinic",
            "Dentists and dental hygienists: what hurts, how long each treatment takes, emergencies straight in.",
            20, 90,
            """
            {
              "instructions": "If the caller is in severe pain, bleeding heavily or has a swollen face, do not book: tell them to come straight in during opening hours or go to emergency care, and take their name and number so the clinic can call back. Ask whether it hurts now before offering times, and give the earliest slot to anyone in pain. First visit: ask them to arrive ten minutes early and bring any previous X-rays. A child must come with a parent.",
              "questions": [
                { "ask": "What is the problem with your teeth, and is it hurting now?", "required": true },
                { "ask": "Have you visited us before?", "required": false },
                { "ask": "Do you have dental insurance?", "required": false }
              ],
              "services": [
                { "name": "Consultation", "minutes": 20 },
                { "name": "Check-up and cleaning", "minutes": 40 },
                { "name": "Filling", "minutes": 60 },
                { "name": "Tooth extraction", "minutes": 40 },
                { "name": "Root canal", "minutes": 90 },
                { "name": "Whitening", "minutes": 60 },
                { "name": "Child check-up", "minutes": 20 }
              ]
            }
            """),

        new BookingTemplate("university", "University — admissions and student office",
            "Prospective and current students and their parents: applications, documents, advising, tours.",
            20, 40,
            """
            {
              "instructions": "Applicants should bring their passport and previous certificates or transcripts. Do not promise admission outcomes, scholarship decisions or deadlines you were not given: the office confirms those at the meeting. Fee, payment and scholarship questions go to the finance desk if one is listed. For a campus tour, ask how many people are coming.",
              "questions": [
                { "ask": "Are you a prospective student, a current student, or a parent?", "required": true },
                { "ask": "Which programme or faculty is it about?", "required": true },
                { "ask": "What would you like to discuss?", "required": false }
              ],
              "services": [
                { "name": "Admissions consultation", "minutes": 20 },
                { "name": "Document submission", "minutes": 20 },
                { "name": "Academic advising", "minutes": 30 },
                { "name": "Tuition and payment query", "minutes": 20 },
                { "name": "Campus tour", "minutes": 40 }
              ]
            }
            """),

        new BookingTemplate("bank", "Bank branch",
            "Accounts, cards, loans and mortgages, with the security rules a bank line must keep.",
            20, 60,
            """
            {
              "instructions": "Never ask for, repeat or write down card numbers, PINs, passwords or one-time codes: say the branch verifies identity in person. Lost or stolen card: tell the caller to phone the card hotline immediately to block it, then book a visit only if they still want one. Remind everyone to bring a passport or ID card; for a business account, the company documents as well. Do not quote interest rates or approval decisions.",
              "questions": [
                { "ask": "Are you an existing customer of the bank?", "required": true },
                { "ask": "What is the visit about: an account, a card, a loan, or something else?", "required": true }
              ],
              "services": [
                { "name": "Open an account", "minutes": 30 },
                { "name": "Card issue or replacement", "minutes": 20 },
                { "name": "Loan consultation", "minutes": 40 },
                { "name": "Mortgage consultation", "minutes": 60 },
                { "name": "Deposit and savings consultation", "minutes": 30 },
                { "name": "Business banking", "minutes": 40 }
              ]
            }
            """),

        new BookingTemplate("support", "Customer support — callbacks",
            "A call centre or help desk booking a callback or a support session at a time the caller chooses.",
            15, 30,
            """
            {
              "instructions": "You are booking a callback or a support session, not a visit: confirm the caller will be reachable on the number they gave at that time. If the issue is urgent (a service is down, or safety is involved), say so in the notes and offer the earliest slot. Do not promise refunds, credits or outcomes; the specialist decides.",
              "questions": [
                { "ask": "Which product or service is this about?", "required": true },
                { "ask": "Briefly, what is the issue?", "required": true },
                { "ask": "Do you have an order, contract or ticket number?", "required": false }
              ],
              "services": [
                { "name": "Callback from a specialist", "minutes": 15 },
                { "name": "Technical support session", "minutes": 30 },
                { "name": "Billing review", "minutes": 20 },
                { "name": "Complaint follow-up", "minutes": 20 }
              ]
            }
            """),

        new BookingTemplate("clinic", "Medical clinic",
            "A doctor's office or clinic: symptoms, first visit or follow-up, and a hard rule for emergencies.",
            20, 40,
            """
            {
              "instructions": "Chest pain, difficulty breathing, severe bleeding, a stroke sign or loss of consciousness: do not book. Tell the caller to call emergency services or go to the nearest emergency department now. Ask whether it is urgent before offering times. Do not give medical advice or diagnoses. Remind: bring ID and previous test results; a child must come with a parent.",
              "questions": [
                { "ask": "What symptoms or concern would you like to discuss?", "required": true },
                { "ask": "Is this a first visit or a follow-up?", "required": true },
                { "ask": "Are you booking for yourself or for someone else?", "required": false }
              ],
              "services": [
                { "name": "Doctor consultation", "minutes": 20 },
                { "name": "Follow-up visit", "minutes": 20 },
                { "name": "Extended consultation", "minutes": 40 },
                { "name": "Check-up", "minutes": 30 },
                { "name": "Vaccination", "minutes": 20 },
                { "name": "Lab test", "minutes": 20 }
              ]
            }
            """),

        new BookingTemplate("salon", "Beauty salon and barber",
            "Hair, nails, skin: long and short services, a preferred master, and the patch-test reminder.",
            30, 120,
            """
            {
              "instructions": "If the caller wants a particular stylist or master, ask for the name and note it. First-time colouring or highlights need a patch test 48 hours before: mention it when they choose either. Say politely that arriving more than 15 minutes late may shorten the service. Do not quote prices unless you were given them.",
              "questions": [
                { "ask": "Do you have a preferred stylist or master?", "required": false },
                { "ask": "Any allergies or skin sensitivities we should know about?", "required": false }
              ],
              "services": [
                { "name": "Haircut", "minutes": 30 },
                { "name": "Men's haircut and beard", "minutes": 30 },
                { "name": "Blow-dry and styling", "minutes": 45 },
                { "name": "Colouring", "minutes": 120 },
                { "name": "Highlights", "minutes": 120 },
                { "name": "Manicure", "minutes": 45 },
                { "name": "Pedicure", "minutes": 60 },
                { "name": "Facial", "minutes": 60 },
                { "name": "Waxing", "minutes": 30 },
                { "name": "Eyebrows and lashes", "minutes": 30 }
              ]
            }
            """),

        new BookingTemplate("legal", "Law firm and notary",
            "Consultations by area of law, deadlines first, and no advice given on the phone.",
            30, 60,
            """
            {
              "instructions": "Do not give legal advice or an opinion on the case: you only book the meeting. Always ask about deadlines: a court date or hearing coming up gets the earliest slot, and say so in the notes. Remind the caller to bring ID and every document relating to the matter. Fees and conflicts of interest are discussed at the meeting; do not quote fees.",
              "questions": [
                { "ask": "What area is it about: family, property, business, immigration, or something else?", "required": true },
                { "ask": "Is there a deadline, court date or hearing coming up?", "required": true },
                { "ask": "Have you worked with us before?", "required": false }
              ],
              "services": [
                { "name": "Initial consultation", "minutes": 30 },
                { "name": "Extended consultation", "minutes": 60 },
                { "name": "Document review", "minutes": 30 },
                { "name": "Notary services", "minutes": 30 },
                { "name": "Contract drafting meeting", "minutes": 60 }
              ]
            }
            """),

        new BookingTemplate("auto", "Car service and repair",
            "Diagnostics, servicing and inspections: the car first, the problem second, safety before times.",
            30, 240,
            """
            {
              "instructions": "If the car sounds unsafe to drive (failing brakes, smoke, overheating, a warning light with a burning smell), advise the caller not to drive it and offer the earliest slot. Say that the final price and time depend on the diagnosis. The registration number is taken at check-in, not on the phone. Cars should arrive ten minutes before the slot.",
              "questions": [
                { "ask": "What make, model and year is the car?", "required": true },
                { "ask": "What is the problem, or which service do you need?", "required": true },
                { "ask": "Roughly what is the mileage?", "required": false }
              ],
              "services": [
                { "name": "Diagnostics", "minutes": 60 },
                { "name": "Oil and filter change", "minutes": 45 },
                { "name": "Tyre change or rotation", "minutes": 45 },
                { "name": "Brake inspection", "minutes": 60 },
                { "name": "Wheel alignment", "minutes": 60 },
                { "name": "Technical inspection", "minutes": 60 },
                { "name": "Pre-purchase inspection", "minutes": 90 },
                { "name": "Full service", "minutes": 180 }
              ]
            }
            """),

        new BookingTemplate("restaurant", "Restaurant reservations",
            "Tables and events: party size first, dietary needs, and how long a table is held.",
            15, 180,
            """
            {
              "instructions": "Ask how many people before anything else. Parties of eight or more are booked as an event; mention that a deposit or a set menu may apply if that is the policy. Tables are held for 15 minutes past the reservation time. Ask whether a high chair is needed when children are coming. Do not promise a specific table. Read the date back with the day of the week.",
              "questions": [
                { "ask": "How many people?", "required": true },
                { "ask": "Any dietary needs or allergies, or is it a special occasion?", "required": false },
                { "ask": "Inside, outside, or a particular area?", "required": false }
              ],
              "services": [
                { "name": "Table for lunch", "minutes": 90 },
                { "name": "Table for dinner", "minutes": 120 },
                { "name": "Birthday table", "minutes": 120 },
                { "name": "Private room or event", "minutes": 180 }
              ]
            }
            """),

        new BookingTemplate("realestate", "Real estate agency",
            "Viewings, valuations and consultations for buyers, renters and sellers.",
            30, 60,
            """
            {
              "instructions": "For a viewing, note the property address or listing reference the caller gives. Ask about budget and timing once, without pressure. Do not quote prices, availability or offers you were not given: the agent confirms at the meeting. Remind the caller to bring ID for viewings and rental applications.",
              "questions": [
                { "ask": "Are you looking to buy, rent, or sell?", "required": true },
                { "ask": "Which area or which property are you interested in?", "required": true },
                { "ask": "What is your budget range?", "required": false },
                { "ask": "When do you need to move?", "required": false }
              ],
              "services": [
                { "name": "Property viewing", "minutes": 30 },
                { "name": "Buyer consultation", "minutes": 45 },
                { "name": "Rental application meeting", "minutes": 30 },
                { "name": "Seller valuation visit", "minutes": 60 },
                { "name": "Mortgage referral consultation", "minutes": 30 }
              ]
            }
            """),
    };
}
