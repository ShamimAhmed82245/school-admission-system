# AI Usage Documentation

AI tools were used throughout the development of this project for technical clarification, planning, brainstorming, prompt preparation, implementation guidance, and frontend development.

The following sections describe how I used AI tools during development and the decisions I made along the way.

## 1. Understanding the .NET Technology Stack

I had not previously worked with .NET. The assignment mentioned `.NET 8+`, so I initially searched for .NET Framework and found references to .NET Framework 4.8.1 and ASP.NET. This created some confusion about which technology I was supposed to use.

I asked GPT to explain the differences between .NET, .NET Framework, and ASP.NET. Through this discussion, I understood the differences and identified ASP.NET Core Web API as the appropriate choice for developing the backend.

## 2. Project Architecture and Initial Setup

After understanding the technology stack, I uploaded the assignment PDF to GPT and provided the following prompt:

**Original prompt:**

> I want to first complete the backend and i will move to frontend. Check this carefully and suggest a floder architecture and i want to add a word at the end of the folder that is shamim82245 just for uniqueness so that they can understand who submitted the project. Currently give the installation steps. after we complete install they we move to coding part

GPT analyzed the assignment and suggested a project folder structure and installation steps.

I followed the suggested structure and created the project folders, including the `shamim82245` suffix for uniqueness.

### Docker-based development environment

Initially, GPT suggested installing the .NET 10 SDK directly on Windows. However, I preferred not to install separate development dependencies on my machine because the assignment explicitly required the entire project to run using Docker.

I suggested using Docker for the development and execution environment instead.

Following this decision, we configured the basic project setup, including:

- Backend and frontend Dockerfiles.
- Docker ignore configurations.
- Git and `.gitignore`.
- Root `docker-compose.yml`.
- Environment variable configuration.
- Project documentation files.

While generating the initial ASP.NET Core project using Docker, I encountered image-related issues with the .NET SDK image. After trying alternative image variants through discussions with GPT, I found that `mcr.microsoft.com/dotnet/sdk:10.0-alpine` worked reliably for my setup.

## 3. Frontend UI Design Using Figma Make

My development plan was to build the backend and frontend separately and integrate them after both were ready.

I wanted Figma Make to generate only the UI and features explicitly mentioned in the assignment PDF, without adding unnecessary functionality.

I provided the following prompt to Cursor:

**Original prompt:**

> i am attaching the document again. i want you give me a master propmt to desing the frontend using figma make. I do not want any unncessary feature. only the requsted ui mentioned in the document will be desinged by figma make. First check the document carefull and give me the promt to desing the frontend ui using figma make.
>
> I development plan is i will desing the backend and frontend separately and later will integrate the api.
> After figma make finish the development i will download and copy in the frontend folder.
>
> only feature mention in the pdf will be generate by figma make.
>
> give me the prompt

Cursor helped prepare a detailed prompt based on the assignment requirements. I then provided that generated prompt to Figma Make.

Figma Make generated the frontend UI based on the requested features. Afterward, I downloaded the generated frontend project and placed it inside the `frontend` directory.

Some minor UI adjustments were still required.

## 4. Database Design and Brainstorming

Before implementing the backend APIs, I studied the assignment requirements and independently brainstormed the database structure.

My initial idea was to use two tables:

- `classes`
- `holds`

I shared my initial database design with GPT to get feedback.

**Original prompt:**

> Now i want to desing the backend api. I have done a small brain stroming and I have found some idea. Since the document clearly mentioned the api endpoints, their request and response parameter we no need to worries about this. I want to share my understanding about the database. I think we need two tables one is classes and another is holds. For the classes table I think it can have id, name and total_seat. On the other hand for holds table I think it can have id, student_id which is student birth registration number, class or class_id which will be foreign key of class table, status which value will be HELD , CONFIRMED , EXPIRED or CANCELLED and expires_at.
>
> status column of holds table can be used as enum. status CONFIRMED means student have made the payment
>
> classes id and holds id has a pattern that i have found in document such as c_123 for classes id and h_457 for holds id
>
> let me know your thoughts

GPT agreed that two tables were sufficient for the initial design but highlighted an important challenge:

> Ensuring that seat availability remains correct when multiple students try to reserve seats simultaneously.

This made me realize that database design alone was not enough. I also needed to consider concurrent requests and prevent the system from allocating more seats than the available capacity.

### My initial seat-counting idea

I then brainstormed a counter-based approach and shared the following idea with GPT.

**Original prompt:**

> so now the challenge is held + confirmed = total_seats. I have an idea. i want to add a count column in holds table and it's initial value will be total_seats value from classes table. when a request is came and it's status value is held or confirmed the the value will be decrease and when the value is 0 we can return 409 no steal available. on the other hand when status is EXPIRED or CANCELLED we can increase the count column value.
>
> for status held or confirmed below two api will be called and we can decrease the count
> post /classes/{classId}/holds
> post /holds/{holdId}/confirm
>
> for status EXPIRED or CANCELLED below one api will be called and we can increase the count  
> delete /holds/{holdId}
>
> when a seat holding time expire for a student or student manually cancel what seat the delete api will be call and count will increase.
>
> so mainly increase or decrese of count table will happen based on api call not for status colmun. i have used the status type here just for understand purpose.
>
> one important note counting value should be get using classes id of holds table latest row for post /classes/{classId}/holds and holds id for post /holds/{holdId}/confirm and delete /holds/{holdId}
>
> this is my idea
>
> if you have better idea then you can share also your one

GPT pointed out an important correction to my initial approach:

- The available seat count should be stored in the `classes` table rather than the `holds` table.
- Creating a new hold decreases the available seat count.
- Confirming an existing hold does not decrease the available seat count again because the seat has already been reserved.
- Cancelling or expiring a HELD reservation increases the available seat count exactly once.
- Database transactions and atomic updates are necessary to handle concurrent reservation requests safely.

I adopted this approach for the database and seat management design.

## 5. Backend API Development Using Cursor

After finalizing the database design, I moved to backend implementation.

The assignment specified eight API endpoints. To make implementation and testing manageable, I divided them into three groups.

I used GPT to analyze the requirements and prepare three separate implementation prompts. I then provided those prompts to Cursor to implement the APIs.

The three implementation groups were:

**Group 1: Class Management**

- Create a class.
- Retrieve all classes.
- Retrieve a class by ID.

**Group 2: Seat Reservation**

- Create a temporary seat hold.
- Retrieve hold details.

**Group 3: Admission Management**

- Confirm an admission.
- Cancel a hold.
- Retrieve the current student's applications.

Each implementation prompt included the relevant API requirements, request and response formats, validation rules, database operations, and testing requirements.

After implementation, I used Cursor to assist with reviewing and testing the backend functionality.

## 6. Frontend and Backend API Integration

Once the backend APIs and frontend UI were ready, the next step was integration.

I used Figma Make to prepare the frontend API integration changes. The integration covered all eight backend endpoints.

The frontend was configured to support two data sources through environment variables:

- `VITE_USE_BACKEND_API=false`: Uses mock data.
- `VITE_USE_BACKEND_API=true`: Uses the actual backend APIs.

The backend API base URL is configurable through `VITE_API_BASE_URL`.

The integration also included student identification through the `X-Student-Id` header, application status updates, automatic polling, and hold countdown functionality.

## 7. AI Tools Used

| Tool          | Purpose                                                                                                                                 |
| ------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| ChatGPT (GPT) | Technology clarification, architecture planning, database brainstorming, concurrency discussion, and implementation prompt preparation. |
| Cursor        | Backend implementation assistance, code review, debugging, and testing.                                                                 |
| Figma Make    | Frontend UI generation and frontend/backend API integration.                                                                            |

## 8. Final Note

AI tools were used as development assistants, but the technical decisions were made through my own study, brainstorming, and evaluation of the assignment requirements.

In particular, the database design and seat-counting approach evolved through my own initial ideas and subsequent discussions with GPT. I also chose the Docker-based development workflow instead of installing the development tools directly on Windows.

I reviewed the generated code, worked through implementation and configuration issues, and used the tools to help complete the project.
