# About





# build 

from project's root:

```
dotnet build RepoTaskManager.slnx
dotnet run --project TaskManager -- "C:\Users\artyo\Documents\5-mine\repo-task-manager"
```

or once built

```
dotnet publish TaskManager -p:PublishProfile=dist
```

# Notes

deletion of work items doesnt cause their removal from open spec - just hides them for now untill the workkflow matures.


# Acknowledgements 

The design system was insipred from work by Mr Pharrell Tan here https://dribbble.com/shots/16333822-Personal-Task-Management-App  - please keep this aknwledgment if cloning this project unless making your own theme. 
