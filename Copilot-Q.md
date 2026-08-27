Agent mode with Claude Opus 4.5

1. fix error "There is already an object named 'AspNetRoles' in the database." when running dotnet ef database update

2. add system wide audittrail  feature in this app
3. add Role based access with User management page to administer User and assign roles to each user.
4. system has 3 roles Admin, Manager and User. Admin has full access to all features, Manager has access to manage users and view audit trails, User has access to view their own data and audit trails.
	•	Default Admin User:
	Username: admin
	Email: admin@aims.local
	Password: Admin@123
5. add a feature to log  all user activities in the system, such as login, logout, data changes to audit trail. This will help in monitoring and auditing user actions for security and compliance purposes.
// Inject IActivityLogger
private readonly IActivityLogger _activityLogger;

// Log a security activity
await _activityLogger.LogSecurityActivityAsync(
    ActivityType.Login,
    "User logged in successfully",
    "Success",
    userId,
    userName);

// Log a general activity
await _activityLogger.LogActivityAsync(
    ActivityType.UserCreated,
    "Created new user 'john.doe'",
    "ApplicationUser",
    userId,
    "Success");

6. create a CRUD page for AssetItem entity. only Admin and Manager can create, update and delete AssetItems, while User can only view the list of AssetItems. The page should display a list of AssetItems with options to create, edit, and delete based on the user's role. 
    
7. implement a search and filter functionality on the AssetItem list page to allow users to easily find specific items based on criteria such as AssetId, Description, or Type. 
  Also add a menu to navigate to the AssetItem management page in the application.
8. add details page for AssetItem throught link from Index page for AssetId. In this details page, allow users to add AssetItemRemarks for the opened AssetItem.
9. include CreateBy column in AssetItemRemarks that is filled with name of the user that input the remarks

10. Create new entity AssetItemDocuments with fields : DocumentTitle, FilePath, CreatedAt, CreatedBy. this entity is to store uploaded documents related to AssetItem.
11. database is updated, now modify asset item Details page to allow Documents upload. adjust the UI to use tabs : tab 1 is for adding remarks and tab 2 is for uploading documents

12. Plant entity
    Just added Plant entity. 
    1. create the dbinitialization script for both Oracle and SQL Server database.
    2. create a crud pages for this
    3. added Plant selection on AssetItem pages
    4. create a Treelist on sidemenu for Plant and AssetTag as subtree menu link .
    5. When Plant tree menu is clicked, show a table of assetitems.
    6. When AssetTag subtree menu clicked, show AssetItem details page.     

13. analyze SIMS Dashboard Rev A 20260701.pptx change the Plant sidemenu tree to design at slide 4
14. change dashboard, 1. move "priority breakdown" to Summary right panel.
15. change dashboard, add Plant Summary section with design from slide 3 of "SIMS Dashboard Rev A 20260701.pptx." This section should display condition of assetitems in each Plant.
 
16. change dashboard, add map as in MapDemo page. same map should swap  "Asset Type", "Recenly Added Assets" sections.  

17. change dashboard, move "Plant Summary" to right panel.
18.  change dashboard, right panel Priority Breakdown should changed to AssetItem Condition breakdown for All Plant
19.  change dashboard, remove 4 boxes above site map section

20. remove Plant Code and Description from AssetItem to Plant entity. Add PlantId as foreign key in AssetItem entity. Update the database accordingly.
 
21.  change asset tag to automatically generate based on <Plant Code> <Equipment Code> - <Equipment order> / <Civil Asset Code> - <Civil Asset Order> format. do not use database function, instead put it in C# code upon assetitem creation and update. asset item page should display the generated asset tag in a read-only field. Update the database accordingly.
  